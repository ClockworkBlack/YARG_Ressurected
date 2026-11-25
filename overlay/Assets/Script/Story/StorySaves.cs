// YARG Resurrected - Story Mode: the three save slots on disk.
//
// Each slot is one small JSON file, story/slot1.json to slot3.json, in the game's own data folder.
// It is written the way the game writes profiles: to a temporary file first, checked, and only
// then moved over the old one (which is kept as slotN.json.bak), so a crash or a full disk
// half-way through a save never leaves a broken file behind.
//
// The game's own data folder is wiped when the app is uninstalled, and new builds of this
// project often have to be installed over an uninstall. So on Android every save is also copied,
// best effort, to Internal storage/YARG/story, next to the songs, where it survives that. When a
// slot is read, whichever copy was played last wins, so a story comes back after a reinstall.
using System;
using System.IO;
using YARG.Core.Logging;
using YARG.Helpers;

namespace YARG.Story
{
    public static class StorySaves
    {
        public const int SLOTS = 3;

        private static bool _mirrorWarned;

        private static string LocalFolder => Path.Combine(PathHelper.PersistentDataPath, "story");

        private static string FileName(int slot) => "slot" + slot + ".json";

        public static bool IsSlot(int slot) => slot >= 1 && slot <= SLOTS;

        /// <summary>
        /// The save in a slot, or null when the slot is empty (or holds nothing readable).
        /// Never throws.
        /// </summary>
        public static StoryProgress Load(int slot)
        {
            if (!IsSlot(slot))
            {
                return null;
            }

            StoryProgress local = null;
            try
            {
                string path = Path.Combine(LocalFolder, FileName(slot));
                local = ReadFile(path);
                if (local == null)
                {
                    // The main file is missing or damaged: the copy kept by the last save may still be good
                    local = ReadFile(path + ".bak");
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not read save slot " + slot);
            }

            StoryProgress shared = null;
            string sharedFolder = SharedFolder();
            if (sharedFolder != null)
            {
                try
                {
                    shared = ReadFile(Path.Combine(sharedFolder, FileName(slot)));
                }
                catch (Exception e)
                {
                    // Shared storage is often not readable (no all-files access): that is fine
                    YargLogger.LogInfo("Story: shared copy of slot " + slot + " not readable: " + e.Message);
                }
            }

            if (shared != null && (local == null || Later(shared.PlayedOn, local.PlayedOn)))
            {
                // The shared copy is newer, so the app was most likely reinstalled. Put it back in the
                // app's own folder so the game does not depend on shared storage from now on.
                WriteLocal(slot, shared.ToJsonText());
                return shared;
            }

            return local;
        }

        /// <summary>Writes a slot now. Returns false (and logs why) when the game's own copy could not be written.</summary>
        public static bool Write(int slot, StoryProgress progress)
        {
            if (!IsSlot(slot) || progress == null)
            {
                return false;
            }

            string text;
            try
            {
                text = progress.ToJsonText();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not put save slot " + slot + " into words");
                return false;
            }

            bool written = WriteLocal(slot, text);
            WriteShared(slot, text);
            return written;
        }

        /// <summary>Empties a slot: the game's copy, its backup and the shared copy.</summary>
        public static void Delete(int slot)
        {
            if (!IsSlot(slot))
            {
                return;
            }

            try
            {
                string path = Path.Combine(LocalFolder, FileName(slot));
                DeleteIfThere(path);
                DeleteIfThere(path + ".bak");
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not delete save slot " + slot);
            }

            // Otherwise the shared copy would bring the deleted story back the next time the slot is read
            string sharedFolder = SharedFolder();
            if (sharedFolder != null)
            {
                try
                {
                    string path = Path.Combine(sharedFolder, FileName(slot));
                    DeleteIfThere(path);
                    DeleteIfThere(path + ".bak");
                }
                catch (Exception e)
                {
                    YargLogger.LogWarning("Story: could not delete the shared copy of slot " + slot + ": " + e.Message);
                }
            }
        }

        // ------------------------------------------------------------------------------------

        private static StoryProgress ReadFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var progress = StoryProgress.FromJsonText(File.ReadAllText(path));
            if (progress == null)
            {
                YargLogger.LogWarning("Story: " + path + " is not a story save, ignoring it");
            }

            return progress;
        }

        // Saves stamp PlayedOn as "yyyy-MM-ddTHH:mm:ssZ", which sorts the same as the times it stands for
        private static bool Later(string a, string b)
        {
            return string.CompareOrdinal(a ?? "", b ?? "") > 0;
        }

        private static bool WriteLocal(int slot, string text)
        {
            try
            {
                return WriteSafely(LocalFolder, FileName(slot), text);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not write save slot " + slot);
                return false;
            }
        }

        private static void WriteShared(int slot, string text)
        {
            string folder = SharedFolder();
            if (folder == null)
            {
                return;
            }

            try
            {
                WriteSafely(folder, FileName(slot), text);
            }
            catch (Exception e)
            {
                // Without all-files access this fails on every save; say so once, not every time
                if (!_mirrorWarned)
                {
                    _mirrorWarned = true;
                    YargLogger.LogWarning("Story: could not copy the save to shared storage (" + e.Message +
                        "). The story is still saved in the game's own folder.");
                }
            }
        }

        // Temporary file, check it reads back, keep the old file as .bak, then move the new one in.
        // The same dance PlayerContainer.SaveProfiles does, so a save is never left half written.
        private static bool WriteSafely(string folder, string name, string text)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name);
            string temp = Path.Combine(folder, Path.GetRandomFileName());

            try
            {
                File.WriteAllText(temp, text);
                if (StoryProgress.FromJsonText(File.ReadAllText(temp)) == null)
                {
                    YargLogger.LogError("Story: a save did not read back correctly, keeping the old one (" + path + ")");
                    return false;
                }

                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", true);
                    File.Delete(path);
                }

                File.Move(temp, path);
                return true;
            }
            finally
            {
                // A step that failed (often on shared storage, where a file from an earlier install
                // may not be ours to replace) must not leave a stray temporary file behind on every
                // save: they would pile up in Internal storage/YARG/story
                try
                {
                    DeleteIfThere(temp);
                }
                catch (Exception)
                {
                    // Nothing more to do; the next save tries again
                }
            }
        }

        private static void DeleteIfThere(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>Internal storage/YARG/story on Android; null everywhere else.</summary>
        private static string SharedFolder()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                return Path.Combine(SongFolderHelper.InternalStorageRoot, "YARG", "story");
            }
            catch (Exception e)
            {
                YargLogger.LogWarning("Story: shared storage not found: " + e.Message);
                return null;
            }
#else
            return null;
#endif
        }
    }
}
