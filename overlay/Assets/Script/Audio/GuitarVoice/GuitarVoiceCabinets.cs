// Guitar voice, part 2c: the speaker cabinets.
//
// WRITTEN BY tools/string-bank/make_cabinets.py. Do not edit by hand: run the tool again.
//
// A guitar speaker is the biggest part of an electric guitar's sound, and far too irregular
// to describe with a few tone controls. So these are measurements of real ones: how a
// cabinet, with a microphone in front of it, answers a single click. Playing the amplifier
// through that answer (GuitarVoiceAmp) puts it through that cabinet.
//
// Measured by Jester Dyne Productions (Bastian Karschewski) and given to the public domain
// (Creative Commons CC0 1.0). The first 2048 samples (46 ms at 44100 Hz) of each, as 16-bit
// numbers packed in base64; see tools/string-bank/README.md.

using System;

namespace YARG.Audio.GuitarVoice
{
    internal static class Cabinets
    {
        /// <summary>Samples in each response (at 44100 Hz).</summary>
        public const int LENGTH = 2048;

        /// <summary>
        /// A Marshall 1960AX cabinet (4x12, built 1998) with Celestion Greenback G12M 25 W speakers,
        /// Shure SM57 microphone, dry room. From "Jester's Emerald Pack" (1: Nacho Guacamole).
        /// </summary>
        public static readonly float[] Greenback = Unpack(GREENBACK_SCALE, GREENBACK_DATA);

        /// <summary>
        /// An oversized 4x12 cabinet with a Celestion Vintage 30 speaker, Shure SM57 microphone.
        /// From "Jester's Brutal Pack" (1: Cookie Monster).
        /// </summary>
        public static readonly float[] Vintage30 = Unpack(VINTAGE30_SCALE, VINTAGE30_DATA);

        private static float[] Unpack(double scale, string packed)
        {
            var bytes = Convert.FromBase64String(packed);
            var response = new float[bytes.Length / 2];
            for (int i = 0; i < response.Length; i++)
            {
                short sample = unchecked((short) (bytes[2 * i] | bytes[2 * i + 1] << 8));
                response[i] = (float) (sample * scale);
            }
            return response;
        }

        private const double GREENBACK_SCALE = 1.377201082312302e-05;
        private const string GREENBACK_DATA =
            "MQBEAmMMuSlOWv9/XXdDR40PWeEywa6ycLk21Wr3FBL1HZUXZwMG7HvfkeFn7O73fALjCn4MHwUO+6r1c/ZM+GD0IuwI50Hpv+++" +
            "97v+PQNKBkkIkAk/CvAHsgDB9C7ojt993kTkgezv9IT8mwChAH7/bv4m/Tb9d/5X/gL97vqi+Lb2+/Zs+VT8ev5j/zr+7/sw+jv6" +
            "Dvym/Wr9Qvs8+Xn5YfsK/Fz74PqG+2z8OP33/Fv7S/kv+Lv4V/or/EH9Nv18/GH8N/2u/qn/AQC5/+H+Bf4F/sv+3f9tAdgDsQaz" +
            "CBsJogd5BDYAP/xA+if7AP73AJACngIaAt8BCQJwAhMDCwQrBc0FbgUFBE8CCQF3AG8A6gDRAfUC6AMvBMgDQwMCA9sC7wJ/A50E" +
            "ywWEBn0GngX0A9QB6f/x/kb/qQByAhsEjQWYBg0H7QZhBqoFGAWkBC4EswNjA3YD3ANTBM0EYgUWBrQG6wa9BpcGzwZbB9YH2wdn" +
            "B5cGcQUMBKIClQE8AZABQALoAjQDEwOuAi8CxgGBAVIBTQFvAbABLwL7AvYD8QSyBRQGFQbPBYIFYwVKBfMEVASOA9QCWAIWAuYB" +
            "nAEgAYkA9/96/zj/Q/94/4//U//f/pv+0/6M/4sAggEmAkoCCQKxAZ4BEAL/AgwEqwR4BIkDTwJFAcEA4QB9AS4CiwJrAuMBNwGz" +
            "AGwASQATAJ//9f5E/q/9S/0r/Uv9gP2Q/W39Vf2N/Sf+A//k/4cAwwCZABsAc//O/mL+W/7P/pv/SABqANj/tv5+/b78yfy2/SX/" +
            "iQB9AcUBWAF2AHX/pv4s/uL9pP1j/Rj91/zM/Pr8Vf2k/Xv9kfwF+2D5VPhf+JP5nPvW/Yf/PQDa/4/+3/xh+5H6qfqC+7z8/f38" +
            "/o3/uv+6/7z/1/8MAEkAZQBBAOD/Vf+v/vz9af0e/RD9Ef30/Jj8+ftH+776hfq0+kr7Hfzw/IH9vv3D/av9iv16/Y/9z/0e/j3+" +
            "Cv6Z/Rr9wvyw/PH8c/31/TP+Iv7i/Z/9ev15/ZX9x/3s/eT9rf1d/Rb9AP0k/Wr9tv37/Uf+sf5H//j/qAA0AXABSQHdAFsA2P9b" +
            "/+D+cf4n/hD+K/58/vD+Xv+c/4r/Lv/C/n3+gf7U/k3/s//r//X/6P/e/9X/zv+8/4X/K//T/pz+jv6n/uX+PP+D/4r/Sv/k/ob+" +
            "XP50/r3+G/90/8n/LwCvADYBqwH8ASACFwLwAcsByAHzAT8CiQKlAoACIQKpAU8BJgEnAUUBcwGaAbABpgGBAU8BDQGvADYAsf9B" +
            "/wr/Ff9K/47/1v8cAFQAcQB6AIoAtADyADMBagGIAXoBNgHfAJ0AiwCiAM0A/QAhATEBNwFHAWQBfAGAAXIBZAFlAXcBkAGfAZMB" +
            "ZQEqAf8A+QAeAV4BnwHSAeQByQGCASYBzgCPAHAAZABaAEgAMgAsAEYAfgC4ANkA2QDEAKsAnACtAPQAXQG9Af4BIAIiAgoC5gHL" +
            "Ad4BIwJ/AsYCygJxAs8BEwF2ABgA8v/m/9n/vv+Z/4H/h/+i/7b/rP+J/2b/WP9o/5X/z/8BACIANAA8AEAASwBoAJ0A4AAhAU4B" +
            "VgE4AQsB5gDUANAAygC0AJQAfAB3AH0AfwBuAEgAEgDe/8X/2f8WAGcAqgC/AJMAMwDM/4v/f/+k/+r/MQBoAIsAngCbAHoAPwD9" +
            "/8T/nv+Y/7H/3P8MADIAPgAlAN//gP8x/wj/A/8U/yz/Pf9G/0r/Rf8//0z/d/+0/+j/CgAlAEYAeACyAOMA8wDbAKkAbgA8ACEA" +
            "IgAzAEIAQwA9AEEAVQBxAIQAgwBsAE8APwBNAH4AxQAJAS8BHwHWAGMA6f+I/1b/Uf9u/5//z//u//P/2v+w/4b/af9a/1P/VP9j" +
            "/3//mv+k/5v/jf+D/4b/lv+v/8v/5P8AACsAYACPAKgAoQB0ACsA1v+Q/3H/fv+o/9r/AgAYAB8AGgATAAkA9P/Q/6D/bP88/xz/" +
            "Gv84/2r/kP+U/3b/Qf8E/9f+zP7j/gv/Mf9N/1//Zf9u/4f/q//G/8v/xP/D/8//3v/j/9f/v/+k/43/fv9z/23/bv94/4r/pv/K" +
            "/+z/AAD//+f/vv+S/3H/ZP9o/2r/Y/9b/1n/Wv9g/2n/bP9i/07/Pf87/0j/Wv9t/4H/mP+1/9v/DgBJAHsAkgCIAGEAMwAXABsA" +
            "PwB3AK0A0QDaAMYAogB5AFAALAAWABMAGgAiAB8ADgDx/8z/nP9l/zP/F/8b/zr/aP+W/7n/y//U/+H/+f8aAEAAZgCDAJYAnwCk" +
            "AKcAqwCzAMMA0QDWANMAzQDEALUAnQCBAGYATgA4ACQADwDx/8n/nP9z/1X/Rv9A/0H/SP9R/2D/d/+W/73/6v8aAEcAagCCAJMA" +
            "oQCuALsAxgDKAMIAsgCkAJoAlACOAIYAegBnAEoAIgD0/8T/mf96/2X/WP9Q/0j/Ov8n/xb/Dv8W/zX/Yf+R/7//5v8EABgAJwA7" +
            "AFUAdACWALIAvgC4AKMAggBiAFEASgBLAE0ARgA0ABUA7v/M/7r/tf+x/5v/bv81/wT/7/4C/zP/cf+t/9r/8P/z/+//8v8FACkA" +
            "UgB9AKUAwgDTANgA0QC/AKgAkgCCAHgAcABkAE0AJADu/7n/lf+G/4f/j/+b/6H/nv+d/6z/0f///yUAOgBCAD8ANwA2AEMAWgBw" +
            "AHoAfAB7AHkAdQBxAGoAWwBBAB8A+f/T/7D/lv+K/4r/kf+S/4j/eP9y/3v/kP+t/8z/6f8EAB4AOQBTAGoAggCaAKgAqQCmAKkA" +
            "sgC6ALsAqgCEAFAAGQDs/87/vf+w/6b/n/+W/43/iP+K/4//kP+L/4P/fv99/3r/dP9z/4P/qv/c/wsAKAAzADMALQAoACsAMwA5" +
            "ADkAMgAqACAAFQAMAAIA+P/u/+j/6//0//P/5f/V/83/0P/U/9H/xf+1/6L/kv+I/4v/nP+4/93/AQAbACYAIwAYAA0ACAAMABgA" +
            "IQAfABAA/v/x/+7/9/8HABYAGwAUAAQA9P/t/+n/4v/X/8v/vv+s/5f/g/95/3z/i/+f/7H/vf/G/83/0//W/9j/3v/n//L//f8M" +
            "ACIAQQBlAIIAkACKAHMAUwAzABgACQAHAAoADAALAAgAAwD8//D/2//D/63/nf+W/5r/pv+0/77/w//E/83/5f8EACEAOwBNAFgA" +
            "XABeAGYAdACCAIwAkgCOAIQAeQBwAG8AdAB4AHIAXwA/ABUA7v/P/7f/p/+l/7L/w//P/9f/3//n/+v/8f/8/wwAHQAuAD8ASgBN" +
            "AEYAOAAvAC0ALwAuACoAKAAxAEUAYQB6AIYAggBwAFcAOwAfAAgA/P/6//j/9P/v/+3/8P/7/woAHAAwAEMATgBSAFAATgBOAE4A" +
            "SwBBACwADgD0/+j/6//1//z/9v/l/9D/w//C/9D/5f/7/wkACgAFAAEABQAVAC0AQgBKAD8AKQASAAsAHAA/AGEAcgBuAFwASgBA" +
            "AD0APQA7ADIAIgAKAPD/3P/U/9j/3v/i/+H/2//R/8v/y//S/9v/5P/q/+//9v8AAA0AHQAuADcANQAqABkABgD4//P/+v8HABEA" +
            "FgAZABgAFgASAA0ACgAEAPn/8v/u/+z/7//1//T/6v/X/8L/t/+9/8n/z//S/9v/7f///w8AFgAWABYAFgATABIAFgAbACQALgAz" +
            "ADAAJwAcAA4A+//p/+L/5P/p/+r/3//I/7H/o/+h/6b/rP+1/8L/zP/O/8f/wP+7/7v/wv/Q/+L/8//9//z/9f/t/+v/6//s//L/" +
            "/P8AAAAA/f/0/+L/z//D/77/vP+5/67/m/+D/3H/bf95/5L/rf/E/9P/2f/b/9v/2v/Z/9T/zP/L/9X/5f/z//v//f/3/+f/1f/N" +
            "/9H/3v/r/+//6P/Y/8f/u/+5/8H/0f/i/+v/7P/p/+z/+f8KABYAGgAWABEADwAOAAwACQABAPj/8P/o/+L/3v/g/+T/5f/e/9H/" +
            "w/+3/7P/t//A/8n/z//R/8v/w/+8/77/y//b/+v//P8KABIAEgAMAAMA+v/0//P/+f8DAAsACQD///L/5//f/9r/2f/Y/9n/2//d" +
            "/97/4v/p//D/8v/u/+j/6P/v//z/CAARABgAHAAcABgAEwASABgAIQAnACQAGgAUABYAIQAtADEAKwAgABgAFAAWAB0AJAAkABsA" +
            "CgD4//H/8//8/wUACQAGAPz/8//w//b/AgAOABQAEgAMAAcACAAPABkAHwAdABUADAAHAAgAEAAaACQAKgAtAC0AKgAmACMAJAAq" +
            "ADUAQQBJAEoAQgAzACEAEAAGAAUABwAKAAwACwAFAPz/8v/w//b///8KABUAGwAdABoAGQAdACYALwAxAC0AJwAhACAAIgAnAC0A" +
            "LwArACQAHQAUAAwACAAIAAoADAAOABEAFQAbAB8AHQAXAA4ACQAIAAwAFAAbAB4AHgAfACQAKwA0ADsAPQA9ADsAOQA2ADUANgA4" +
            "ADkANAAsACIAHAAdACQALgA0ADIAJwAWAAUA+v/1//X/+f/9/wEABQAKAA4AEAARABAADwAOAA4ADwAPAA0ADAALAAsACgAIAAUA" +
            "AgAAAP//AAAEAAoAEQAYAB4AIAAfABkADwAFAAAAAQAHAAwAEAATABQAEwASABEADwALAAUA///8//z/AAAEAAgACQAFAP3/8//q" +
            "/+T/4v/k/+n/7v/y//X/9f/y/+//7f/v//P/+P/7//z//P/7//n/+f/7//7/AAD///v/9f/u/+n/6P/r/+//8//1//X/9P/0//T/" +
            "9P/0//L/7//r/+j/5v/m/+j/6v/s/+7/8f/0//f/+v/9////AAD///////8BAAIAAgAAAP7//P/8//v/+f/3//P/8P/u/+//8v/0" +
            "//X/9f/z//H/8P/v/+//7//x//P/9v/4//n/+v/8//3///8BAAMABAAEAAMAAgACAAMABAAGAAYABQADAAAA/f/7//r/+f/5//j/" +
            "+f/6//v//f/+//7//v/9//3//v8AAAEAAQABAP///v/9//3//v/+//7//f/8//r/+f/6//z//f/+/////v/+//3//P/7//v/+v/6" +
            "//r/+v/6//v//P/+////AAAAAAEAAAABAAEAAgACAAMAAwADAAIAAgABAP///v/9//3//P/8//z//P/9//3//f/+////////////" +
            "AAAAAAAAAAAAAAAA//8AAAAAAAAAAAAAAAAAAAEAAQABAAEAAAAAAAAA/////wAA/////////////wAAAAAAAAAAAAABAAEAAQAB" +
            "AAEAAQABAAEAAQABAAEAAQABAAEAAQABAAEAAQABAAEAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

        private const double VINTAGE30_SCALE = 1.3089739482037361e-05;
        private const string VINTAGE30_DATA =
            "OAD7AYcI9BhZNm9b6nr/f7Bfkycv7zHFMrdfyuHtkgkZEqAIgvZB5zbi/ug39En92QOwBkEDkP9S/G34kvbQ80rrx+Ip32XgXObD" +
            "7RPzxvTU9aH2B/o+AYYJOg/dEJUOnAfJ+3TuZeSw4XPn8vAt+I/5yvUa8Njr0+vL8Dz36foK+q31WPFc8MLyTva598b1EfIK7wXv" +
            "zfJK+I78Sf/4APEBIAM1BUwHMQj2Bn4Djf7Q+Djz0u4Y7Zvu0vKD+Jr+OgMNBdkEdgNpAYAAlQHCAzgGKAfnBLP/4vmK9g/4Bf4H" +
            "BicNFRH2EJwNwQgwBAIBO/+A/nX+4f5r/7//6f8PADQAegAlAVQC9wOyBd8GLgdjBpEEFgKC/5j9T/3Q/jUBeQPFBI8EKgPDAToB" +
            "8AHpA1oG9wcKCJ8GPQTEAej/7P7d/qf/IwFHA84FCQhzCboJ+QiqB0MGKQXXBE0F/gVRBvMFHgVGBP0DrAQ3BvQHOQnOCcAJTwnB" +
            "CEoI3gclB8sF5wPSAQUA3P5d/mX+w/5A/5r/yf/Z/8j/h/9I/yf/Hf8r/1z/nv/R/9//0v8AAKgA3gGFAyIFIQZGBpYFVwQLAw4C" +
            "fwFgAZsB+wFJAmoCRwLiAV0B+ADaAPQALgGVASsC3QKKAwUEFASPA6sCywEyAf4AKwF8Aa8BtAGpAbkBDQKcAhgDNgPyAn8CDQKy" +
            "AXEBOQHaADMAcP/V/nv+Pv7u/Wj9nPyi+776QvpP+tD6l/t7/FT9Ef63/mT/KgDwAJsBEgJaAnsCeQJbAjYCFwL7AeYB1wHHAakB" +
            "cAERAaMASQAZAAUA8f/F/3X/C/+S/iP+2/3Q/QL+Uv6f/t7+/v7u/sH+n/6l/sr+4P67/lL+rv3l/Cf8pPtw+4H7vfv4+yX8U/yi" +
            "/Cb94P29/pH/KABZABsAgv+z/sr95Pwi/Kf7k/vv+5n8XP0P/or+wv6z/mX++f2e/W79cP2f/fP9cf4V/9D/fAD2AC0BKwEHAdcA" +
            "ugC7AMgAuwBxAN//If9p/tz9iv1k/UX9GP3k/MX84vxD/c39VP6m/qn+g/5u/o7+2P4d/zT/IP/9/tz+uP6L/k/+8/1u/cv8KvzA" +
            "+7z7J/zP/Gj9xP32/R3+Sf6Y/g7/mv8SADsA+v+B/x//E/9v/xQAuQALAesAeAAFAOj/OQDJAGABygHzAfEB1gGkAW4BQQEdAQAB" +
            "2wCpAG0AIADH/2j/Cv/M/rb+mv5a/gn+1P3h/Tj+w/5T/6z/tf+G/1b/Wv+g/w8AfgC/AM8A1gDqAAsBMQFYAY0BygH/ASwCUAJb" +
            "AlMCNALgAWcB4wBhABQAHwB1AAIBjgHUAb0BSgGtADcAEwA9AIoAtwCuAHkAIQDM/4j/Sf8H/7f+Tv7k/av9uf0G/n7+IP/X/3EA" +
            "xADEAIYANQD1/93/CgBmAJsAXgDM/zL/xP6b/r7+MP/i/5oALQGqARsCfALJAvoCDwMUA/YCsAJeAggCqgFHAdAAQgDH/33/Zv91" +
            "/4P/c/9g/23/pP/w/0cAuAAsAYYBxAHXAZgBEQGCABwAzv9e/8f+T/4i/j3+lv4K/27/o/+Y/1z/F//l/tj+7P4P/zj/d//b/3oA" +
            "NAHIASECSwI/AgICswFrAS0B0gBNAMT/af9O/2j/qf8CAFgAnQDTAA4BWQGlAeEB+wHqAcgBwwH2AUYCdgJoAiECqAEdAa8AcABl" +
            "AG8AUQAUAPX/CABAAJYA6gAZARoB8QCjAEkA9v+7/6f/uv/s/yoAUABFABYA1v+S/1v/Ov88/1//j/+8/+j/FAA7AFEASAAnAP3/" +
            "yv+S/2//d/+n//D/PwB8AIsATQDC/w//b/4R/gz+UP61/hL/Xf+X/8n/AQA6AGIAcwBjADMAAQDk/+H/9/8RABsAFQD2/7T/Zf8s" +
            "/xv/Nv9r/6L/0f/t/+//3//N/7n/lP9L/93+av4Y/vj9/f0O/g3+9f3d/db97v0w/pT+D/+E/8z/3f/J/7D/rf/A/8r/w/+7/77/" +
            "3v8aAFwAjwChAJoAkgCKAIsApwDbAA8BNQFAASkB8gCvAIAAbQBfAEMAJgAdABkA+f+//4L/Xv9p/5v/1v8NAC4AJQDz/63/fP9w" +
            "/4H/qP/S/9j/uf+C/0j/J/8m/zb/Tv90/6v/5/8UACQADgDY/6H/cv9F/xv/9v7Y/sD+ov6G/nP+Y/5T/k7+Y/6Z/uv+UP+v//j/" +
            "JwBEAFQAUgA1AA8A8v/U/6r/gf9u/37/s/8GAGYAvAD7ACUBNwE7AVABigHkAU0CowLIAq0CVALVAV4BDwH1AAABCgH1AMQAhgBQ" +
            "ACgABwDz//P/AAAOAB8ALgAvACUAFAADAP//EQAwAFoAhQCSAHwAYQBQAEYAQQBFAE8AVgBSAEgASwBZAF4AVQBPAF8AfwCTAJIA" +
            "cAAcAKn/OP/a/pL+Z/5c/nL+ov7S/vf+Ef8a/w3/+v4S/3X/+P9jAKwA0ADFAIcAJwDS/7X/2P8TADEAKAAXACAAQgBlAHMAUgAD" +
            "ALD/hP+R/9L/LgB8AJ8AoQCMAGEAKgD0/7z/h/9V/yP/+v7s/gv/Wv/I/y4AcgCZALMAuQCmAI8AgAB3AG8AXQA7AAYAu/97/2T/" +
            "ef+S/43/b/9N/zL/Lf9L/4r/3/83AHcAiABrADkADQDc/6T/eP9k/2T/aP92/6P/5v8gAEAASQA6AB8ACwAAAAkAJwBVAI4AxwDz" +
            "AAsBDwEDAeoAwwCfAJMAnACeAIMAWgA2ABsAEgAnAFkAmADDANYA4QDpAOkA4QDPALYAnQCIAHcAaABYAEMAJgAGAOn/x/+c/4T/" +
            "mv/N////LwBmAJUAoACIAGUATgBJAFcAewCnAMMAygDCALkAuQC/ALoAogBzADAA7P+5/6T/q/+8/8X/w/+7/6z/l/+D/3n/fP+F" +
            "/4j/h/+H/4b/i/+d/7b/1P/w//v/8P/O/5T/XP9H/17/mP/Z/wAAAgDh/6z/hP+A/6P/2P/3/+3/yP+c/3j/b/+M/87/IgBoAH8A" +
            "WQAHAKf/R//+/ur+Ef9c/6T/x/+8/5L/Yv9T/3v/z/83AJsA3gD6APMA3gDXAO0AGwFDAVMBTwE9AR0B7QC3AIcAYABBACoAGgAQ" +
            "AAQA7//V/8n/yP/J/8P/uf+y/6z/pv+l/7D/xf/f/+//7//k/9D/uP+i/4z/fv+A/5b/uf/d//D/7f/d/8r/w//S//z/NwByAJYA" +
            "mAB5AEkAGQD3/+P/1//L/7j/oP+G/27/Yv9q/4b/tv/w/yQARgBRAEkAOQAsACMAGQAQAAYA+P/o/9j/wv+p/5T/kv+n/87/+/8m" +
            "AEwAaAB6AH4AcABXAEEAOwBDAE0AUgBEAB0A7v/N/8D/vv+y/5L/cP9g/2H/av9z/3f/df93/4z/s//j/wkADgDp/6r/bP9G/0H/" +
            "Uv9i/1z/RP8t/yn/Pf9g/33/jf+L/4H/i/+4//X/IwAnAAAA0v+4/8T/+f9DAHoAgQBhAEkAWQB+AJIAeQAwANj/lP95/4j/tP/c" +
            "/+T/x/+g/5H/pP/K/+r/8f/d/8D/rP+3/+n/LQBcAGMATgA6ADgAOwAvABQA+v/k/9L/yf/O/97/6v/q/+7/BgAnAEAAPwAgAPX/" +
            "z/+3/7H/u//G/7r/jv9P/xX/7f7X/tD+0v7d/u7+A/8f/zr/Uv9s/4n/tv/1/zcAZwBxAFIAJAADAP//JwBwALcA4wDsANgAsQCB" +
            "AFsATQBPAFAATwBPAE8ATQBJADwAIwABAOX/1v/S/93/+P8aADcASQBYAGAAWgBOAE0AXwB0AHUAXgBCADAAGQD3/+D/5P/9/xAA" +
            "EwARABIAEAAIAPz/8v/k/9L/xP+7/7P/pf+e/6n/w//i//n/BgATACsASQBgAG0AeQCVAMYA+AAWARQB8QC9AJAAbwBVAD4AJwAT" +
            "AAgABgARACkAPQBEAD8ANQA0AEYAYwCBAJsApQCVAG0AOgAQAPP/4P/P/73/qv+N/2b/Rf86/1P/iv/A/9z/3//i/wIAOABnAHEA" +
            "UAAVANT/m/93/3L/kv/T/x4AXQCCAI8AkACGAHQAZABYAFAATQBNAE8ATwBLAD8AMAAeAAoA9//m/9n/2f/l//P/+//9//v/+f/5" +
            "//v//v/4/+D/vf+h/57/tv/W/+X/5P/d/9f/2P/t/xcASAB2AJYApwCnAJ4AkgCJAIEAdgBmAFMAPQAhAAQA6v/S/8H/tv+s/6D/" +
            "k/99/1P/I/8C//3+IP9e/5r/wP/N/8n/zP/r/x8AVgB6AIcAhAB4AGwAbgCEAKAArQCgAIAAWwAyABAAAAD//wkAGAAnADIAMgAp" +
            "ABgABAD8/woAKQBGAFsAawB7AIkAmACoALEAqACEAFQAKQANAAQADQAdACsAOQBHAFUAYQBgAE0AMAASAP3//f8IAA8ADAAAAPT/" +
            "6v/b/8j/uP+x/7L/t/+9/8b/zP/K/8H/sP+b/4//kv+i/7f/x//Q/9b/1//R/8H/qP+J/2b/Sv89/z//Tf9f/2//e/+C/4b/h/+H" +
            "/43/nv+x/73/wf/D/8j/y//J/87/4/8JADEARQA/ADEAJgAeABcAFAAYACAAGQD5/8r/nf+I/5P/sf/S/+3/+f/3/+7/6P/p/+n/" +
            "4f/V/8z/x//E/8T/xv/P/+L//f8dAEEAaACHAJIAhwBsAEwAKgAPAPz/8f/y//f/9//0//f/BQAaACEAHgAeAB4AHQAcABsAJAA7" +
            "AE0AUwBRAD4AJQAVAA8AIQBFAGIAbQBhAEUAMAAiABgAGgAdABcAAADS/5r/cf9m/4L/uv/s/wQA9//O/6X/iv+I/6b/zv/w/wkA" +
            "EwAOAAQA8f/W/73/rf+r/8D/4/8MAC0AOgA4ADIAMQBEAGUAfwB9AF4ALwAFAOr/3//k//X/BgANAAgA+//u/+f/4//i/+H/2//T" +
            "/8n/vP+0/7X/wP/Q/+D/7v/5/wAABQAIAAkACAAJAA8AGAAfACAAHAAXABIAEAARABAADgAOAA0ADQAQABMADwAHAP3/9f/w/+//" +
            "8v/4////BAAKAAwADAAHAAEA+v/2//T/9f/2//X/8v/w//L//P8JABMAEwALAPz/7v/l/+f/7//6/wMABgAEAAEA/P/4//b/+P/7" +
            "////BQAKAA0ADAAIAAUABAAFAAYABwAIAAgABgAGAAkADQAPAA4ADAALAAsADQAOAA4ADgANAAsACgALAA0ADgAOAAsABwADAAAA" +
            "/f/8//z//P/+////AQADAAMAAwABAP/////+//////8AAAAAAQACAAQABQAGAAUABAAEAAQABAAEAAMAAgAAAAAA//////7//v/+" +
            "//7//v/+//7///8AAAAAAQACAAMAAwADAAMAAwADAAIAAQAAAAAAAAABAAEAAQABAAEAAQAAAAEAAQABAAEAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";
    }
}
