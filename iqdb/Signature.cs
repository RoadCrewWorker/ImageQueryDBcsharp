using System.IO;
using System;

namespace iqdb
{
    public abstract class Signature
    {
        #region 16b hash
        public static byte[] StringToByteArray(String hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        public void SetHash(string h)
        {
            if (h == null){ this.m_Hash = new byte[16]; return; }
            if (h.Length != 32) throw new InvalidDataException("Hash length invalid");
            this.m_Hash = StringToByteArray(h);
        }
        protected byte[] m_Hash; //16 byte md5 hash

        public byte[] Hash
        {
            get { return m_Hash; }
        }
        public string HashString { get { return this.m_Hash == null ? "unknown" : BitConverter.ToString(this.m_Hash).Replace("-","").ToLower(); } }
        #endregion

        private uint m_ID; //temporary id assigned by BuckedGrid
        public uint ID
        {
            get { return m_ID; }
            set { m_ID = value; }
        }

        protected uint[] m_SigData;
        public uint[] SigData { get { return m_SigData; } }

        public abstract float Sum_Weights();
        public abstract float Get_Coeff_Weight(uint coeff);

        public abstract void Serialize(BinaryWriter w);
        public abstract void Deserialize(BinaryReader r);

        protected Signature() { }
        public Signature(BinaryReader r)
        {
            this.Deserialize(r);
        }
    }
}
