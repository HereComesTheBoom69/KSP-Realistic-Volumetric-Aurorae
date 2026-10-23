// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using UnityEngine;

namespace RVA
{
    /// <summary>
    /// A 64 x 64 blue-noise tile for the march's per-pixel sample offset (1.1.0). Made
    /// once, offline, with Ulichney's void-and-cluster method (toroidal, sigma = 1.5, every
    /// grey level exactly 16 times) and embedded here so the shader bundle needs no
    /// texture asset. Why not the interleaved gradient noise the shader used before:
    /// wherever the march under-samples a feature (a 12 km patch crossed by two 7 km
    /// steps, a far band's thin ribbons), each pixel's result swings with its offset,
    /// and the offset pattern itself shows through. Gradient noise is a fine diagonal
    /// grating, so what showed were stripes; blue noise has no structure below a few
    /// texels, so what shows is grain, and the low-res upsample's smoothing takes most
    /// of that out.
    /// </summary>
    public static class AuroraBlueNoise
    {
        public const int Size = 64;

        static Texture2D texture;
        static bool tried;

        /// <summary>The tile as a point-sampled, repeating R8 texture; null if it could not be made.</summary>
        public static Texture2D Texture
        {
            get
            {
                if (texture == null && !tried) { tried = true; texture = Build(); }
                return texture;
            }
        }

        static Texture2D Build()
        {
            try
            {
                byte[] raw = Convert.FromBase64String(Data);
                if (raw.Length != Size * Size) { Debug.LogWarning($"[RVA] blue-noise tile: {raw.Length} bytes, expected {Size * Size}"); return null; }
                var tex = new Texture2D(Size, Size, TextureFormat.R8, false, true)
                {
                    name = "RVA blue noise 64",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat,
                    anisoLevel = 0,
                    hideFlags = HideFlags.HideAndDontSave
                };
                tex.LoadRawTextureData(raw);
                tex.Apply(false, true);
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RVA] blue-noise tile could not be created ({e.GetType().Name}: {e.Message}), falling back to gradient noise");
                return null;
            }
        }

        // 64 x 64 bytes, row-major, base64
        const string Data =
            "8xxtQKP2SnHA+4Bo8BF7NSC52qRjHDWIJkbQU+I1Ysd4DW4xxtyNK8+s/nY/mSPRVzG1bM2si/K6Mskc9LEof8ZbuukHZy7gAlwa" +
            "zyxZ6dKbXT0ReNSs3HPvHYOwAfI/nOJH/RJeuwKASZEo4bto+wDJkEVdE9dRFqtGkHgOaEUAlS55mMqzj6c024ikwkaGC/KO5MKU" +
            "ThafuGbIKXCM1Cdakqt5QaL2NNgNWa4SgziiduwK4Z42b4f/X+A2u+eMqfdL2SNTFUHrdrhDBm8etmaqI1c0BGv/OgeTQvahTBS0" +
            "7wTRKuWFU3K5oPN6Td3BXCE7rnokv+ihKgTEm1QX0zdyE7+C/9FhhyNW45H9zTHfSL6B6rPPgFjW6F0N3MFfdzOGZLcXzQ/nImM5" +
            "xCyVFvKK01X7kWEOSM5pgyL5fV+23lmrOmujCb73Eao2XZ2AAHT6D2RCJJy+LIatNn0j/J3FSes6lV+rP5fWiAXtardBpW8Xxjnf" +
            "gbTvOLDZQ6IkiQeX8BmSLdlGlWfSexZJ7qg5ltOliPMXc0gSy2q7jkEJ3h2ldvUmwX78GVbQqVJ/COAttU4DqVYcdZhSCG7I6E/C" +
            "K3bPT+mxdDTEJuyz1SPDYhxQLd9VsdjqmflQGOWvcFSP0ApP2m0DTbh0mh41/8temvSJbMr4L9MSvvGRGDuBZuJCugKAXQ7woUyN" +
            "B3FXiemxeL0TbzoGX3olp9FdMO2+Kme2iTSj4pMv6kHevYwiR3wS1SiaQo5k44AyWbL8060SjWL5nCXcghhewD2Z+DAN2D71lsyJ" +
            "qsM1i0UEhKEUftk9/xfMWx/IZAmFWw9ur+bEOFnpeQjCpkgdod11CzJK9CSqN8lJuTnQ9nfkG7dNoGWGBF4k/FEb1PG52m34TKsA" +
            "llWsdPFDfvmu16DxPpYBaKG9G6zxWCX6bM1BJZ3DepfYdBThe2CWrQ0vpGLceMgeqOS2Rdp0oGcMV5kePMth4XDpK40HuJk6Ikp3" +
            "LNBY+CjfjEppM9aGtgGKuvBj4ARdO8VXkQr9IG9Tjs1HApI671Myep4NL+pDsn0u5bWNIja6FEvE4FgYwmnME7uGGrZ7QA7ny5gQ" +
            "cz7oXBRNhKIu7bca8rAxvUHU7rIhgv8q03K6FdRhvoPJkBX+yFAPd/afgNWjZjOpd+SO9Khi5UnXnmKxfSO6TtylKpjRrR5Ez26F" +
            "o0Z/a+efgwU55mu/qVoJl/qKO/FOHd9vOpNpp9dTB14++g2H80gCLlU8DJlxNAbM7zZb/ZEfxmv1NXX+W5MMUSraAM9RE13GeFKe" +
            "Dj+E40ZqJLEDpF4sobwD7SpCvOnMJJZxyifXtZ6B0uwps/yLUxejiQNk5kkIhVMOvCPrsMj7Z5wlj7Yt9Kwa1WP0zh6gyVjgccF/" +
            "0fNIXNJ0khxwiqzhUhaSXG/9HWi9h1se23fCRtWzOXmVu9iv5YvWO3MUiTi/9T7gZopE4jOSJrF5N+0RkTL7Qg5oIYi2EfOxME8L" +
            "OcDspzsMyEymBUjJnUCsK/ZtJfDDFzNgH0FpA6dX3UurF1l1yx2iCLp/x01oAMCBqUzZGYm1mcfnNKFHYsr+uYFmLH2334gw5Hj2" +
            "FG3tCmKWEoChVtun7HifxYL0lSvF7oCXBqpP+29a7BCm2/pEYCm5bJ5T7jhzFlV+2wKacyCh0BL5TSGbZcM7k9Qwhb/lTcngPgtz" +
            "Q5AL+i9IHLl8C2gm0usvhsc0myR2OIYhnNXzCM4u3QFf1KX4vSiK0jtX50aXYcp29A6uI1+xS6Qhe7IuXozA9CbOVLRkx+RfP+aj" +
            "VLlHauIPt95R889YtnMTkHxap3e+KIVDCmfjThXtvAiH3TYApT1X7HzgCPtb0kIEn/8dqWWCrRTelHYQrNOONP8SkrEhekKLBa6V" +
            "F+cyVMQ87BtE/5Phs3s3lK1gf6RqKrRw6b+D1BiPQMB2FIvwaNB5N98ASuhuPCT8UCxwAcpsgDnxXNOnZsQvbEjJn/ytJNeDsmUQ" +
            "UyDuzBH7wRs299IUnE4eZy6hzWosmdo1u5gZR7ZaksQvi8umhMCc8EuqI7/cBZ0y/RbigPcNh2IERm+WB9Awo8VunEuFJ0abyVCB" +
            "Q/OP3LL/TAWq8kmzYyNT64rVKfmiGfJbBWc72B+I42CaS2/MUokpXD+jvNgpfeC/XPdMdvI+2wO6ZdV45WEDpb8tXws8inXkXR2H" +
            "Cvd9wgalcA5jP3m1R+KvEXhctz4U+Su0ghHavpPPG1U48bSPEzekHLaMJX9bM/KqDDCN2G0a1Xufzhu4KMHccM2kQN5hN+69h+TP" +
            "E5sr78eW9wnScY/GG+g+rG4B8HSNqGoeUu7MheNZDMHno84Vk03utSP9U63tR2j0V5FBnzRXFZMpgs9PIaoxVW3bh1kiTjWnglPs" +
            "OGOfVvYuT7Qn3gjNQaB0KWg/0JZoRBuHP27GXHtBmoI5Bb4qpw/odwH6tdZq87ITmnbzFpHDCj6p5nPB4iaeArB90AqWf9idZUP6" +
            "gb3fAJXGEfUxre50tf7XH64K3LsU0ZZ14IY20LBeiiNLqAdZ6EDAXdlJr/5+1AORGGNDyt9PJe9rILk8DcWRWRMxXq7+ULN9VQjT" +
            "K1EEnzeP8y1YavZNHFnHbk4Z7Mh75jF1kccuiwGicjYdYi1JpPp+tBVslL9Epd5Y/HnqH7aZ7n0hOnEcoOKLYZ3DgF3pbEelyo0l" +
            "seqdCfyOoTJFC5e6/kUX3Wv5J8jomLz0bM40Uu2IM/4OhC/GjRmqTjDSbUjE24nmvkQnyhr3Pd4ivw/QeQDlPHzEMEW5JtppvlXY" +
            "HWKtfJ5SskaGCFOCE6+GDNknqNNaseRQBGk3zm+I5hWlCVadEGTwdatJhQttp02J+CeeVKoMaI/XYHoGq/eOcTrOBeokzxHsZ6nd" +
            "PuQmXL6XZQRDex9vmfKw45QHsl8893m7K883kwLbZ7zslNYvs14+w3H9zk3zEqDtP4EUKOejg1e5P3eQN9Ufc8RNn/E6dujDkvTI" +
            "OdJ+J15C+CjFlSDWQm31r1LALZofOFgReOYTk98aM5chgbQsVMDeYrROD9swjPVcw59QuTCUAHvVD0usL1USn2MMSL8XotZPdeNX" +
            "hKYGiyCA/VzmfMyv/EPGaq5NhmG73jtlyo0ZnDHKkvpkqxrXBir7DIfwY800tI33HYTluSrhr/2P4GaDGasKtjLrxlrdPhKmRQhk" +
            "iyOjiCb2BsjwC3Wq5QL2cEjreiA8dcFQm2u0fF3gGKz+VSFqyFzWcEWNd1AzcQC1NM6PRf5mGUmbuHLKjNi470rXA1TUNnqmQJNX" +
            "HUt/OKfUCVew0QHsLIPnSMwynUNvgpjiPaIRNqkF68MWpssp9VXqbCrClXrlLwvsJWY1GJotdupnupXqWynOs/mb17ggZLyY8kqO" +
            "otQ+D6Uei9W2KBW+B3nqkMH7YiaXWtqERp58Da/bVAKnzGuHrVGj+IFWsMY9qA9LHXLjEH0xbQxakvw7GoEoaRVdt/Vz3GQGUOlf" +
            "20msVilyR4fSP/YOaOMdwTyXIITwPR5W+zjRAHDC4gqKHvh+2cWwj0zbwkXnyyuE323G5Kn/eyaUTzW89HSgOIwi+bsK16QZt26t" +
            "LpSzW9Rn+EvPYbPekMAZe+g8Ikds01ycKGk7Af9lH5MXd6BRA6xEEVc3zUfBBOSoFpQsx6vRboM4ju5aMuMDgMc+/AuLK7t9FZgs" +
            "dwlEmluzjabxljHhTsHopIItoLxZ9K417cV61pW8hwucZYnLXING4BD1UgLfYMsUe8SQSu5YGHtN6qQF5DrD91Ks7s8k2g5YvRN8" +
            "tAeOFF1Jyu08gNEQaI4fWy7naSXt2C3wQCL9wGR9JJe4IKlMnT//ZSicz6vcmDZyVq1rhhHUZzGDbUj3hCjNYPxCdfKv3RZzBpso" +
            "S927QPWmBkaudFWyFX9rrwidPcVrNfd36SW2DKbbE3AvY7cdxNon60iiIY+9BajINXDpSqUbndIzIYViteVUsf1+CJlyx4X6yxKU" +
            "RMag1jTdVesQicxFDope33JRvzyO8QJJ9oFBnAy3YN9B+FjmGJe1CIziLWS9UZnGQpIgzGw4ol/OGlIrYJ436G73Ak6OHoOqSuZd" +
            "oNivMcmTIn7oVsJ50ZMVZMt0/zDFfhWcMH1R12U4un/aA3L5DOAy84QPwxnbMuqSuQ162R6GKmK373HOMXmwBSpxTPkBRPOrCZgh" +
            "pC1Uqu4kilGVGmqz2WTD+yWp7w9MlOk9slt7qGBElu9ReLFFbuLNTK1ZvpnhOaYMWvsa1pXxvRZ/pGXEMW3WQv1q3Qe8PdMA5arx" +
            "SQ2IQAKQRHnLXyarhSvRHMAC1HKuH5D6AKgXO4z+CtVHEW3GKZy4ay9hP43lVtWLEuJQsoUPyItGeJhhtTpXJ3rQ7Ka9bNUcmv+8" +
            "Fmrwkkv9nCjlO2TKKYFh8J4iajN0sIT4THzjQu2ozR5prDQftz2deBrnWDWv5Cn6FNmAkcmfORxaLPJStDNvQtNSDbgzZoRTtwqj" +
            "3UfA1lF5yd6l6ibQHJbUAI8Zg0/6wQbcdP5gyfIuwKF8HF/AT6RwIfUJYbp12pt/E+GMBOehieByoOcVyW/4iFoVlzIbt0EEUIxg" +
            "QL1mMFa9OeELmX49nk2GJwZMjmkBze2WCYnrNrlK2Bj8kEsJyz2oWMd6MB/FQAfQLqhHHjPsd7L5iuZlmMIY8aMJ6Kv/cMplsi1Z" +
            "ye8PsOKZudk6+01wPdFsHMtfmHKuQCLpuGL5cifxTLFj+VSwiF7rfNiUvARMaQ6oLfV+Oc10kESCEJggQ/V22yGPa9NAeWMQp4cl" +
            "orUq4UioBOcnhcdnpi+JGJm4C4/dDpt+JfQ9mgjCY0Lknto6WdIbabMOVNwiulDarYrBE6ZKuzJaGfUs6Vi/1xL2Wppz/o4+01Xv" +
            "AXzZUcU71mxAwnM32btuG99SLakbfSbHdbqIT92b+y/CbvQzegVWNGvrAX/7osuQr4EYQ2N6jAfCMha4Zw+jMZhFshDrXYQb/KQX" +
            "708Bk8iueP2KzfFSkBb+AbAkP1+CpQeeXubF8KHPkFbHInEMTjjF3JryNrtP3IHRR3zzvnHjIP5zoCveslYriF6g0kUsYRHTSgZo" +
            "pjXmRJ1s7ZHQGutK0RiSK2cYRCbjmkHetepgA3EprhrlnyJhq+gpkRdOzI1bN78KlEZ94cy1J2n4iOmfOXKXuh/DZn/ZML4Nc64z" +
            "a4e0ReG4gvp2rQpmjSp3ofiKVMl2QW3uPBKbVMo6sWkHqtWCavbIA2Y7EOR8C7ZRH7DjLvZa1gmzG1mFQ/hVv98g/XeVUA3EWza8" +
            "71PXEMwjP+ENlM4CtIvFcQX6ft4u9Uoj6lQ8JJ7yrpVDxJs12oNgwxB4QIWaTuvHotglnQOUPGAJN6ndjCDShBmbOa9KkbynZfxO" +
            "fyz3S+O3XqUQl3W9khan2biGUB1v7lgaZsEF70mMqeEX+Sd1OgZjfuZuT86i1w==";
    }
}
