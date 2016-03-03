using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace iqdb
{
    /*
     * 1. 20x20 pixel
     * 2. RGB-> YUV
     * 
     * 5,5,6: 16 bit, 
     * 3. encode: U>>3, V>>3, Y>>2 : 1-400 
     * */

    public class ColorSignature : IntegerSignature
    {
        const int SIDE_BITS = 5; //width 32, so 1024 pixels total
        const int SIDE_LENGTH = 1 << SIDE_BITS;
        const int PIXEL_BITS = SIDE_BITS << 1;
        const int PIXEL_COUNT = 1 << PIXEL_BITS;

        const int Y_BITS = 6; const int U_BITS = 5; const int V_BITS = 5;
        const int YUV_BITS = Y_BITS + U_BITS + V_BITS;
        public const int COLOR_COUNT = 1 << YUV_BITS;

        public static uint getColorID(double y, double u, double v)
        {
            //Y: double [0 , 255]            //I: double [-128 , 128]            //Q: double [-128 , 128]
            uint yi = ((uint)Math.Floor(y)) >> (8 - Y_BITS); //now 6 bits: 0-63
            uint ui = ((uint)Math.Floor(u * 0.838 + 128)) >> (8 - U_BITS); //now 5 bits 0-31
            uint vi = ((uint)Math.Floor(v * 0.956 + 128)) >> (8 - V_BITS); //now 5 bits 0-31
            uint cid = (yi << (U_BITS + V_BITS)) | (ui << V_BITS) | (vi);
            //if (cid >= COLOR_COUNT)               Console.WriteLine("{0} {1} {2} {3} {4} {5} {6}", cid, y, yi, u, ui, v, vi); 
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
            //uint p = (c & 1023) + 1;
            //c = c >> 10;

            double v = (c & 31) << (8 - V_BITS);// / 0.838;
            v -= 128;
            v /= 255;
            c = c >> V_BITS;

            double u = (c & 31) << (8 - U_BITS);// / 0.956;
            u -= 128;
            u /= 255;
            c = c >> U_BITS;

            double y = c << 2;
            y /= 255;
            double r = (y + 0.956f * u + 0.621f * v) * 255;
            double g = (y - 0.272f * u - 0.647f * v) * 255;
            double b = (y - 1.107f * u + 1.704f * v) * 255;

            return getHexUint(r) + getHexUint(g) + getHexUint(b);
        }
        public string GetHexData()
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < this.m_SigData.Length; i++)
            {
                b.Append(getHexUint(weights[i]>>2));
                //b.Append('-');
                b.Append(getColorsHex(this.m_SigData[i]));
                //b.Append(' ');
            }
            return b.ToString();
        }
        public static ColorSignature FromFileName(string hash, string fn)
        {
            Image org = Image.FromFile(fn);
            ColorSignature sig = ColorSignature.FromImage(hash, org);
            org.Dispose();
            return sig;
        }
        private ushort[] weights;

        public static ColorSignature FromImage(string hash, Image org)
        {
            Bitmap bit = new Bitmap(org, new Size(SIDE_LENGTH, SIDE_LENGTH));
            BitmapData bmd = bit.LockBits(new Rectangle(0, 0, SIDE_LENGTH, SIDE_LENGTH), ImageLockMode.ReadOnly, bit.PixelFormat);
            IntPtr ptr = bmd.Scan0;

            //1.b Read RGBs
            int pixel_index = 0;

            double[][] img = new double[3][];
            img[0] = new double[PIXEL_COUNT];
            img[1] = new double[PIXEL_COUNT];
            img[2] = new double[PIXEL_COUNT];
            unsafe
            {
                for (int y = 0; y < SIDE_LENGTH; y++)
                {
                    byte* row = (byte*)bmd.Scan0 + y * bmd.Stride;
                    for (int x = 0; x < SIDE_LENGTH; x++)
                    {
                        img[0][pixel_index] = (byte)row[x * 4 + 2];
                        img[1][pixel_index] = (byte)row[x * 4 + 1];
                        img[2][pixel_index] = (byte)row[x * 4];
                        pixel_index++;
                    }
                }
            }

            //1.c Remove image objects
            bit.UnlockBits(bmd);
            bit.Dispose();
            bit = null;

            //2.b in place YIQ transform
            double Y, I, Q;
            uint[] histogram = new uint[COLOR_COUNT];
            uint[] ids = new uint[COLOR_COUNT];
            for (uint i = 0; i < COLOR_COUNT; i++) ids[i] = i;

            for (pixel_index = 0; pixel_index < PIXEL_COUNT; pixel_index++)
            {
                double p_r = img[0][pixel_index], p_g = img[1][pixel_index], p_b = img[2][pixel_index];
                Y = 0.299 * p_r + 0.587 * p_g + 0.114 * p_b;
                I = 0.596 * p_r - 0.275 * p_g - 0.321 * p_b;
                Q = 0.212 * p_r - 0.523 * p_g + 0.311 * p_b;
                //uint x=;
                //string t=getColorsHex(x<<10|0);
                //Console.WriteLine(t);
                histogram[getColorID(Y, I, Q)]++;
            }
            //Y: double [0 , 255]            //I: double 0.596 * [-128 , 128]            //Q: double 0.523 * [-128 , 128]
            Array.Sort(histogram, ids);

            uint[] sigdata = new uint[50];
            ushort[] weights = new ushort[50];
            uint o = COLOR_COUNT - 1;
            for (int i = 0;  i < sigdata.Length; i++)
            {
                sigdata[i] = ids[o - i];  // 6 bit Y, 5 bit U, 5 bit V, 10 bit weight 0-1023
                weights[i] = (ushort)(histogram[o - i] - 1);
            }
            ColorSignature sig = new ColorSignature(hash, sigdata, weights);
            
            return sig;
        }

        public ColorSignature(string md5, uint[] features, ushort[] weights) : base(md5, features) { this.weights = weights; }

        public ColorSignature(BinaryReader r)
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
            return "MD5 " + this.HashString + "-> ColorSignature: " + this.SigData.Length + " " + this.weights.Length;
        }
    }
}
