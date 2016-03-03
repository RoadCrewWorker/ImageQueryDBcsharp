using System.IO;

namespace iqdb
{
    public class HaarSigBlockDB
    {
        private ushort[][] sig_y, sig_i, sig_q;
        private float[][] weights_c;
        public static ushort[][] DeserializeBlockf(string file)
        {
            
            BinaryReader binaryreader = new BinaryReader(File.OpenRead(file));
            int s = (int)(binaryreader.BaseStream.Length / sizeof(ushort) / 30);
            ushort[][] sigdata = new ushort[s][];
            int i = 0;
            while (binaryreader.BaseStream.Position < binaryreader.BaseStream.Length)
            {
                sigdata[i] = new ushort[30];
                for (int j = 0; j < 30; j++) sigdata[i][j] = binaryreader.ReadUInt16();
                i++;
            }
            return sigdata;
        }
        private int m_Count = 0;
        private float[] m_weightsums;
        public int Count { get { return this.m_Count; } }
        public HaarSigBlockDB(string file_y, string file_i, string file_q)
        {
            this.weights_c = new float[3][];
            for (int c = 0; c < 3; c++)
            {
                this.weights_c[c] = new float[1 << HaarSignature.PIXEL_BITS];
                for (int i = 0; i < weights_c[c].Length; i++)
                {
                    int cid=(c << (HaarSignature.PIXEL_BITS) | i);
                    this.weights_c[c][i] = HaarSignature.WeightLookUp[cid];
                }
            }

            this.sig_y = HaarSigBlockDB.DeserializeBlockf(file_y);
            this.sig_i = HaarSigBlockDB.DeserializeBlockf(file_i);
            this.sig_q = HaarSigBlockDB.DeserializeBlockf(file_q);
            if(sig_y.Length!= sig_i.Length || sig_i.Length!=sig_q.Length) throw new InvalidDataException("Length of signature blocks doesn't match.");
            
            this.m_Count = this.sig_y.Length;
            this.m_weightsums = new float[this.m_Count];
            for (int i = 0; i < m_Count; i++)
            {
                float sum=0;
                foreach (ushort c in this.sig_y[i]) sum += this.weights_c[0][(c >> 1)];
                foreach (ushort c in this.sig_i[i]) sum += this.weights_c[1][(c >> 1)];
                foreach (ushort c in this.sig_q[i]) sum += this.weights_c[2][(c >> 1)];
                this.m_weightsums[i] = sum;
            }
        }

        public ushort[][] getSig(int i)
        {
            return new ushort[][] { this.sig_y[i], this.sig_i[i], this.sig_q[i] };
        }

        public static float CompareShortArrays(ushort[] sig1, ushort[] sig2, float[] weights)
        {
            float w_match = 0;
            int index1 = 0, index2 = 0;
            while (index1 < sig1.Length && index2 < sig2.Length)
            {
                ushort coeff1 = sig1[index1];
                ushort coeff2 = sig2[index2];

                if (coeff1 <= coeff2) index1++;
                if (coeff2 <= coeff1) index2++;
                if (coeff1 == coeff2) w_match+=weights[(coeff1 >> 1)];
            }
            return w_match;
        }

        public float InternalSimilarity(int i, int j)
        {
            float overlap = HaarSigBlockDB.CompareShortArrays(this.sig_y[i], this.sig_y[j], this.weights_c[0]) +
                HaarSigBlockDB.CompareShortArrays(this.sig_i[i], this.sig_i[j], this.weights_c[1]) +
                HaarSigBlockDB.CompareShortArrays(this.sig_q[i], this.sig_q[j], this.weights_c[2]);
            return overlap / (this.m_weightsums[i] + this.m_weightsums[j] - overlap);
        }
    }
}
