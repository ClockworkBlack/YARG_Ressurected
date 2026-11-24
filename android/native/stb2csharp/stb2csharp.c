// YARG Resurrected - Android build of YARG's STB2CSharp image loader.
// Exposes the same two entry points YARG.Core's YARGImage.cs imports.
#define STB_IMAGE_IMPLEMENTATION
#define STBI_NO_STDIO
#include "stb_image.h"

#define EXPORT __attribute__((visibility("default")))

EXPORT unsigned char* load_image_from_memory(const unsigned char* data, int length,
                                             int* width, int* height, int* components)
{
    return stbi_load_from_memory(data, length, width, height, components, 0);
}

EXPORT void free_image(unsigned char* image)
{
    stbi_image_free(image);
}
