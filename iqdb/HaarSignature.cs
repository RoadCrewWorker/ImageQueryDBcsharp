/***************************************************************************
		imgSeek ::	Haar 2d transform implemented in C/C++ to speed things up
														 -------------------
		begin								: Fri Jan 17 2003
		email								: nieder|at|mail.ru
		Time-stamp:						<05/01/30 19:58:56 rnc>
		***************************************************************************
		*		Wavelet algorithms, metric and query ideas based on the paper				*
		*		Fast Multiresolution Image Querying																	*
		*		by Charles E. Jacobs, Adam Finkelstein and David H. Salesin.				 *
		*		<http:// www.cs.washington.edu/homes/salesin/abstracts.html>					*
		***************************************************************************

		Copyright (C) 2003 Ricardo Niederberger Cabral

		Clean-up and speed-ups by Geert Janssen <geert at ieee.org>, Jan 2006:
		- introduced names for various `magic' numbers
		- made coding style suitable for Emacs c-mode
		- expressly doing constant propagation by hand (combined scalings)
		- preferring pointer access over indexed access of arrays
		- introduced local variables to avoid expression re-evaluations
		- took out all dynamic allocations
		- completely rewrote calcHaar and eliminated truncq()
		- better scheme of introducing sqrt(0.5) factors borrowed from
			FXT package: author Joerg Arndt, email: arndt@jjj.de,
			http:// www.jjj.de/
		- separate processing per array: better cache behavior
		- do away with all scaling; not needed except for DC component

		To do:
		- the whole Haar transform should be done using fixpoints

		This program is free software; you can redistribute it and/or modify
		it under the terms of the GNU General Public License as published by
		the Free Software Foundation; either version 2 of the License, or
		(at your option) any later version.

		This program is distributed in the hope that it will be useful,
		but WITHOUT ANY WARRANTY; without even the implied warranty of
		MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.	See the
		GNU General Public License for more details.

		You should have received a copy of the GNU General Public License
		along with this program; if not, write to the Free Software
		Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA	02111-1307	USA
*/

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace iqdb
{
    public class HaarSignature : IntegerSignature
    {
        static int COEFFICIENT_COUNT = 30;
        public const int CHANNEL_COUNT = 3;

        const int SIDE_BITS = 7;
        const int SIDE_LENGTH = 1 << SIDE_BITS;
        public const int PIXEL_BITS = SIDE_BITS << 1;
        const int PIXEL_COUNT = 1 << PIXEL_BITS;
        public const int CHANNEL_SIZE = PIXEL_COUNT << 1; //adds sign bit

        public static HaarSignature FromFileName(string hash, string fn)
        {
            Image org = Image.FromFile(fn);
            HaarSignature sig = HaarSignature.FromImage(hash, org);
            org.Dispose();
            return sig;
        }

        public static HaarSignature FromImage(string hash, Image org)
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
            for (pixel_index = 0; pixel_index < PIXEL_COUNT; pixel_index++)
            {
                Y = 0.299 * img[0][pixel_index] + 0.587 * img[1][pixel_index] + 0.114 * img[2][pixel_index];
                I = 0.596 * img[0][pixel_index] - 0.275 * img[1][pixel_index] - 0.321 * img[2][pixel_index];
                Q = 0.212 * img[0][pixel_index] - 0.523 * img[1][pixel_index] + 0.311 * img[2][pixel_index];
                img[0][pixel_index] = Y;
                img[1][pixel_index] = I;
                img[2][pixel_index] = Q;
            }
            //Y: double [0 , 255]            //I: double [-128 , 128]            //Q: double [-128 , 128]
            //3. Signature generation per channel

            uint[] sigdata = new uint[CHANNEL_COUNT * HaarSignature.COEFFICIENT_COUNT];
            float[] avglum = new float[CHANNEL_COUNT];
            for (uint channel = 0; channel < CHANNEL_COUNT; channel++)
            {
                //3.a Haar Wavelet transformation
                HaarSignature.TransformHaar2D(img[channel]);
                avglum[channel] = (float)(img[channel][0] / PIXEL_COUNT);

                //3.b
                uint[] t = HaarSignature.GetSignificantIndices(HaarSignature.COEFFICIENT_COUNT, img[channel]);
                for (uint j = 0, l = (uint)(channel * HaarSignature.COEFFICIENT_COUNT); j < t.Length; j++, l++)
                {
                    sigdata[l] = ((channel << PIXEL_BITS | t[j]) << 1) | (uint)(img[channel][t[j]] > 0 ? 1 : 0);
                }
                img[channel] = null;
            }
            HaarSignature sig = new HaarSignature(hash, sigdata, avglum);
            img = null;
            return sig;
        }

        public static float[] WeightLookUp = null;
        public static float[, ,] Weights = new float[,,]
        {	
            // For scanned picture (sketch=0):
            //    Y      I      Q       idx   total      occurs
            {
                { 5.00f, 19.21f, 34.37f},  // 0   58.58      1 ('DC' component)

                { 0.83f,  1.26f,  0.36f},  // 1    2.45      3
                { 1.01f,  0.44f,  0.45f},  // 2    1.90      5
                { 0.52f,  0.53f,  0.14f},  // 3    1.19      7
                { 0.47f,  0.28f,  0.18f},  // 4    0.93      9
                { 0.30f,  0.14f,  0.27f}   // 5    0.71      16384-25=16359
            },
	
            // For handdrawn/painted sketch (sketch=1):
            {
                { 4.04f, 15.14f, 22.62f},
                { 0.78f,  0.92f,  0.40f},
                { 0.46f,  0.53f,  0.63f},
                { 0.42f,  0.26f,  0.25f},
                { 0.41f,  0.14f,  0.15f},
                { 0.32f,  0.07f,  0.38f}
            }
        };

        public static void InitializeCoefficientTranslation()
        {
            HaarSignature.WeightLookUp = new float[CHANNEL_COUNT * PIXEL_COUNT];
            for (uint channel = 0; channel < CHANNEL_COUNT; channel++)
            {
                for (uint y = 0; y < SIDE_LENGTH; y++)
                {
                    for (uint x = 0; x < SIDE_LENGTH; x++)
                    {
                        uint cid = channel << PIXEL_BITS | y << SIDE_BITS | x;
                        HaarSignature.WeightLookUp[cid] = HaarSignature.Weights[1, Math.Min(5, x > y ? x : y), channel];
                    }
                }
            }
        }

        #region Members / Properties
        private float[] m_AverageLuminance;
        public float[] AverageLuminance { get { return m_AverageLuminance; } }

        public override float Sum_Weights()
        {
            float sum = 0;
            foreach (uint signature_coefficient in this.SigData)
            {
                sum += HaarSignature.WeightLookUp[signature_coefficient >> 1];
            }
            return sum;
        }
        public override float Get_Coeff_Weight(uint coeff) { return HaarSignature.WeightLookUp[coeff >> 1]; }

        public bool IsGrayScale { get { return Math.Abs(m_AverageLuminance[1]) + Math.Abs(m_AverageLuminance[2]) < 6 * HaarSignature.PIXEL_COUNT / 1000; } }
        #endregion

        public HaarSignature(string md5, uint[] features, float[] avglum) : base(md5, features) { this.m_AverageLuminance = avglum; }
        public HaarSignature(BinaryReader r)
            : base(r)
        {
        }

        #region Construction

        //Static helper functions
        private static void NormalizeHistogramm(ref double[] channel)
        {
            int i = 0;
            double sum = 0;
            int[] hist = new int[256];
            int[] lookup = new int[256];
            for (i = 0; i < PIXEL_COUNT; i++) hist[(int)channel[i]]++;
            for (i = 0; i < 256; i++) lookup[i] = (int)(255 * (sum += hist[i])) >> PIXEL_BITS;
            for (i = 0; i < PIXEL_COUNT; i++) channel[i] = lookup[(int)channel[i]];
        }
        private static void TransformHaar2D(double[] a)
        {
            int i;
            double[] t = new double[SIDE_LENGTH >> 1];

            // Decompose rows:
            for (i = 0; i < PIXEL_COUNT; i += SIDE_LENGTH)
            {
                int h, h1;
                double C = 1;

                for (h = SIDE_LENGTH; h > 1; h = h1)
                {
                    int j1, j2, k;

                    h1 = h >> 1;		// h = 2*h1
                    C *= 0.7071;		// 1/sqrt(2)
                    for (k = 0, j1 = j2 = i; k < h1; k++, j1++, j2 += 2)
                    {
                        int j21 = j2 + 1;

                        t[k] = (a[j2] - a[j21]) * C;
                        a[j1] = (a[j2] + a[j21]);
                    }
                    // Write back subtraction results:
                    for (int j = 0; j < h1; j++)
                        a[i + h1] = t[j];
                }
                // Fix first element of each row:
                a[i] *= C;	// C = 1/sqrt(NUM_PIXELS)
            }

            // Decompose columns:
            for (i = 0; i < SIDE_LENGTH; i++)
            {
                double C = 1;
                int h, h1;

                for (h = SIDE_LENGTH; h > 1; h = h1)
                {
                    int j1, j2, k;

                    h1 = h >> 1;
                    C *= 0.7071;		// 1/sqrt(2) = 0.7071
                    for (k = 0, j1 = j2 = i; k < h1; k++, j1 += SIDE_LENGTH, j2 += 2 * SIDE_LENGTH)
                    {
                        int j21 = j2 + SIDE_LENGTH;

                        t[k] = (a[j2] - a[j21]) * C;
                        a[j1] = (a[j2] + a[j21]);
                    }
                    // Write back subtraction results:
                    for (k = 0, j1 = i + h1 * SIDE_LENGTH; k < h1; k++, j1 += SIDE_LENGTH)
                        a[j1] = t[k];
                }
                // Fix first element of each column:
                a[i] *= C;
            }
        }

        private static uint[] GetSignificantIndices(int num_coeffs, double[] cdata)
        {
            uint i = 1;
            uint[] sig = new uint[num_coeffs];
            int heap_size = 0;
            while (i < num_coeffs + 1)
            {
                sig[heap_size] = i++;
                HaarSignature.HeapSiftUp(sig, cdata, heap_size++);
            }
            while (i < PIXEL_COUNT)
            {

                double vnew = cdata[i];
                if (vnew < 0) vnew = -vnew;

                double vzero = cdata[sig[0]];
                if (vzero < 0) vzero = -vzero;
                if (vnew < vzero) { i++; continue; }
                sig[0] = i;
                int current = 0, next = 1;
                while (true)
                {
                    next = (current + 1) * 2 - 1;
                    if (next > heap_size) break;

                    double va = cdata[sig[next]];
                    if (va < 0) va = -va;

                    if (next + 1 < heap_size)
                    {
                        double vb = cdata[sig[next + 1]];
                        if (vb < 0) vb = -vb;
                        if (va > vb)
                        {
                            va = vb;
                            next++;
                        }
                    }
                    if (vnew <= va) break;
                    //Else do the switch, loop back
                    uint t = sig[next];
                    sig[next] = sig[current];
                    sig[current] = t;
                    vnew = va;
                    current = next;
                }
                i++;
            }
            return sig;
        }
        private static void HeapSiftUp(uint[] idx, double[] val, int i)
        {
            if (i == 0) return;
            int j = (i - 1) / 2;
            if (Math.Abs(val[idx[i]]) < Math.Abs(val[idx[j]]))
            {
                uint t = idx[j]; idx[j] = idx[i]; idx[i] = t;
                HaarSignature.HeapSiftUp(idx, val, j);
            }
        }
        #endregion

        #region Serialization
        string ixx(float[] v) { string r = ""; foreach (float vs in v)r += vs + ","; return r; }

        public override string ToString()
        {
            return
                "MD5 " + this.HashString + "->"
                + "AverageLuminance: " + ixx(this.AverageLuminance) +
                " Signature: " + this.SigData.Length;
        }

        public void ToSQL(StreamWriter w)
        {
            StringBuilder tb = new StringBuilder();
            tb.Append(this.HashString).Append('\t');
            foreach (float f in this.AverageLuminance)
            {
                tb.Append(f.ToString()).Append('\t');
            }
            tb.Append("\"{");
            foreach (uint f in this.m_SigData)
            {
                tb.Append(f.ToString()).Append(',');
            }
            tb.Append("}\"");
            w.WriteLine(tb.ToString());
        }

        public override void Serialize(BinaryWriter w)
        {
            if (this.Hash != null)
            {
                w.Write((uint)0);
                w.Write(this.Hash);
            }
            else
            {
                w.Write(this.ID);
            }

            w.Write((byte)(this.m_AverageLuminance.Length));
            for (int i = 0; i < this.m_AverageLuminance.Length; i++)
            {
                w.Write(this.m_AverageLuminance[i]);
            }

            w.Write((ushort)this.SigData.Length);
            for (int i = 0; i < this.SigData.Length; i++)
            {
                w.Write(this.SigData[i]);
            }
        }
        public override void Deserialize(BinaryReader r)
        {
            this.ID = r.ReadUInt32();
            if (this.ID == 0)
            {
                this.m_Hash = r.ReadBytes(16);
            }
            else
            {
                this.m_Hash = new byte[16];
            }

            byte avglc = r.ReadByte();
            if ((avglc != 1 && avglc != 3))
            {
                throw new InvalidDataException("Deserialization: " + avglc);
            }
            this.m_AverageLuminance = new float[avglc];
            for (int i = 0; i < avglc; i++)
            {
                this.m_AverageLuminance[i] = r.ReadSingle();
            }

            ushort siglength = r.ReadUInt16();
            this.m_SigData = new uint[siglength];
            for (int i = 0; i < siglength; i++)
            {
                this.m_SigData[i] = r.ReadUInt32();
            }
        }

        #endregion

        #region Comparison

        public static float CompareIntArrays(uint[] sig1, uint[] sig2, float[] weights)
        {
            float w_match = 0;
            int index1 = 0, index2 = 0;
            while (index1 < sig1.Length && index2 < sig2.Length)
            {
                uint coeff1 = sig1[index1];
                uint coeff2 = sig2[index2];

                if (coeff1 <= coeff2) index1++;
                if (coeff2 <= coeff1) index2++;
                if (coeff1 == coeff2) w_match += weights[coeff1 >> 1];
            }
            return w_match;
        }

        public float Compare(HaarSignature other)
        {
            float w_match = 0, avge = 0f;
            for (int c = 0; c < this.m_AverageLuminance.Length; c++)
            {
                avge += HaarSignature.Weights[1, 0, c] * Math.Abs(this.m_AverageLuminance[c] - other.m_AverageLuminance[c]);
            }
            w_match = CompareIntArrays(this.m_SigData, other.m_SigData, HaarSignature.WeightLookUp);
            Console.WriteLine("avge:" + avge);

            return w_match / (this.Sum_Weights() + other.Sum_Weights() - (w_match)); //Range 0-1
        }
        public float CompareTo(out int usedcoeffs, HaarSignature other, bool ignore_color)
        {
            float w_match = 0, w_total = 0;
            int num_colors = 0;
            //ignore_color || this.AverageLuminance.Length == 1 || other.AverageLuminance.Length == 1 ?                1 : 3;

            ignore_color = num_colors == 1;
            for (int c = 0; c < num_colors; c++)
                w_match -=
                    HaarSignature.Weights[1, 0, c]
                    * Math.Abs(this.m_AverageLuminance[c] - other.m_AverageLuminance[c]);

            int
                u = 0,
                b1 = 0,
                b2 = 0,
                r1 = this.m_SigData.Length,
                r2 = other.m_SigData.Length;
            while (r1 > 0 || r2 > 0)
            {
                uint i1 = r1 > 0 ? this.m_SigData[b1] : int.MaxValue;
                uint i2 = r2 > 0 ? other.m_SigData[b2] : int.MaxValue;
                u++;
                int diff = (int)i1 - (int)i2;
                if (diff <= 0)
                {
                    b1++;
                    r1--;
                }
                if (0 <= diff)
                {
                    b2++;
                    r2--;
                }

                i1 = i1 >> 1;
                i2 = i2 >> 1;

                if (
                    ignore_color
                    &&
                    (i1 >> 15 > 0 || i2 >> 15 > 0)
                    )
                    continue;
                if (i2 < i1) i1 = i2;
                float weight = HaarSignature.WeightLookUp[i1];
                w_total += weight;
                if (diff == 0) w_match += weight;
            }
            usedcoeffs = u;
            return w_match / w_total;
        }
        #endregion
    }
}
