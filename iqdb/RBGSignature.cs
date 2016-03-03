using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.IO;

namespace iqdb
{
    /*
     * 1. 20x20 pixel
     * 2. RGB-> YUV
     * 
     * 5,5,6: 16 bit, 
     * 3. encode: U>>3, V>>3, Y>>2 : 1-400 
     * */

    public class RGBSignature : IntegerSignature
    {
        const int SIDE_BITS = 4; //width 16, so 256 pixels total
        const int SIDE_LENGTH = 1 << SIDE_BITS;
        const int PIXEL_BITS = SIDE_BITS << 1;
        const int PIXEL_COUNT = 1 << PIXEL_BITS;

        const int CHANNELBIT = 8-3; //5b
        const int COLORBITS = CHANNELBIT + CHANNELBIT + CHANNELBIT; //15b
        public const int COLOR_COUNT = 1 << COLORBITS; //32k

        public static uint getColorByte(uint c)
        {
            return c >> (8 - CHANNELBIT);
        }
        public static uint getColorID(uint r, uint g, uint b)
        {
            uint cid = (getColorByte(r) << (CHANNELBIT + CHANNELBIT)) | (getColorByte(g) << (CHANNELBIT)) | getColorByte(b);
            return cid;
        }

        public static string getHexUint(double d)
        {
            uint b = (uint)d;
            if (b > 255) b = 255;
            string h = b.ToString("X2");
            return h.PadLeft(2, '0');
        }
        public static string getColorsHex(uint c)
        {
            uint mask = (1 << CHANNELBIT) - 1;
            uint r = (c & mask) << (8 - CHANNELBIT);

            c = c >> CHANNELBIT;

            uint g = (c & mask) << (8 - CHANNELBIT);

            c = c >> CHANNELBIT;
            uint b = c << (8 - CHANNELBIT);
            return getHexUint(r) + getHexUint(g) + getHexUint(b);
        }

        public string GetHexData()
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < this.m_SigData.Length; i++)
            {
                if (weights[i] > 0)
                {
                    b.Append(getHexUint(weights[i]));
                    b.Append(getColorsHex(this.m_SigData[i]));
                }
            }
            return b.ToString();
        }
        public static RGBSignature FromFileName(string hash, string fn)
        {
            Image org = Image.FromFile(fn);
            RGBSignature sig = RGBSignature.FromImage(hash, org);
            org.Dispose();
            return sig;
        }
        private ushort[] weights;

        public static RGBSignature FromImage(string hash, Image org)
        {
            Bitmap bit = new Bitmap(org, new Size(SIDE_LENGTH, SIDE_LENGTH));
            BitmapData bmd = bit.LockBits(new Rectangle(0, 0, SIDE_LENGTH, SIDE_LENGTH), ImageLockMode.ReadOnly, bit.PixelFormat);
            IntPtr ptr = bmd.Scan0;

            //1.b Read RGBs
            int pixel_index = 0;

            uint[] histogram = new uint[COLOR_COUNT];
            uint[] ids = new uint[COLOR_COUNT];
            for (uint i = 0; i < COLOR_COUNT; i++) ids[i] = i;
            unsafe
            {
                for (int y = 0; y < SIDE_LENGTH; y++)
                {
                    byte* row = (byte*)bmd.Scan0 + y * bmd.Stride;
                    for (int x = 0; x < SIDE_LENGTH; x++)
                    {
                        uint r = (byte)row[x * 4 + 2];
                        uint g = (byte)row[x * 4 + 1];
                        uint b = (byte)row[x * 4];
                        histogram[getColorID(r,g,b)]++;
                        pixel_index++;
                    }
                }
            }

            //1.c Remove image objects
            bit.UnlockBits(bmd);
            bit.Dispose();
            bit = null;

            //2.b in place YIQ transform
            Array.Sort(histogram, ids);

            uint[] sigdata = new uint[50];
            ushort[] weights = new ushort[50];
            uint o = COLOR_COUNT-1;
            for (int i = 0; i < sigdata.Length; i++)
            {
                if (histogram[o - i] <1) continue;
                sigdata[i] = ids[o - i];
                weights[i] = (ushort)(histogram[o - i]);
            }
            RGBSignature sig = new RGBSignature(hash, sigdata, weights);
            
            return sig;
        }

        public RGBSignature(string md5, uint[] features, ushort[] weights) : base(md5, features) { this.weights = weights; }

        public RGBSignature(BinaryReader r)
            : base(r)
        {
        }

        //TEMP
        public override void Serialize(BinaryWriter w)
        {
            base.Serialize(w);

            w.Write((ushort)this.weights.Length);
            for (int i = 0; i < this.weights.Length; i++)
            {
                w.Write(this.weights[i]);
            }
        }

        public override void Deserialize(BinaryReader r)
        {
            base.Deserialize(r);

            ushort weightslen = r.ReadUInt16();
            if (weightslen != this.m_SigData.Length) throw new InvalidDataException("Weight and data length dont match up.");
            this.weights = new ushort[weightslen];
            for (int i = 0; i < weights.Length; i++)
            {
                this.weights[i] = r.ReadUInt16();
            }
        }

        public override string ToString()
        {
            return "MD5 " + this.HashString + "-> RBGSignature: " + this.SigData.Length + " " + this.weights.Length;
        }
    }
}
