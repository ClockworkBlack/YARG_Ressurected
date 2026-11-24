using System;
using YARG.Settings.Types;
using YARG.Venue.Drawn;

namespace YARG.Settings.Types
{
    /// <summary>
    /// Which venue songs are played in: a random one, a random one of a kind, a particular one, or
    /// none (the game's own background). Stored as the venue's id or one of the VenueCatalog choices.
    /// </summary>
    public class VenueChoiceSetting : DropdownSetting<string>
    {
        public VenueChoiceSetting(string value, Action<string> onChange = null)
            : base(value, onChange, localizable: false)
        {
        }

        protected override void SetValue(string value)
        {
            // A venue that no longer exists falls back to random
            bool known = value == VenueCatalog.CHOICE_RANDOM || value == VenueCatalog.CHOICE_RANDOM_STREET ||
                value == VenueCatalog.CHOICE_RANDOM_UPSCALE || value == VenueCatalog.CHOICE_NONE ||
                VenueCatalog.Find(value) != null;
            base.SetValue(known ? value : VenueCatalog.CHOICE_RANDOM);
        }

        public override void UpdateValues()
        {
            _possibleValues.Clear();
            _possibleValues.Add(VenueCatalog.CHOICE_RANDOM);
            _possibleValues.Add(VenueCatalog.CHOICE_RANDOM_STREET);
            _possibleValues.Add(VenueCatalog.CHOICE_RANDOM_UPSCALE);
            foreach (var venue in VenueCatalog.All)
            {
                _possibleValues.Add(venue.Id);
            }
            _possibleValues.Add(VenueCatalog.CHOICE_NONE);
        }

        public override string ValueToString(string value) => VenueCatalog.Describe(value);
    }
}
