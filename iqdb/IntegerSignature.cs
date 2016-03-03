using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace iqdb
{
    public class IntegerSignature : Signature
    {
        //Method to create IntegerSignature from input txt
        public IntegerSignature(string md5, uint[] features)
        {
            this.SetHash(md5);
            //Array.Sort(features);
            this.m_SigData = features;
        }
        public IntegerSignature(BinaryReader r)
        {
            this.Deserialize(r);
        }
        public IntegerSignature(string line)
        {
            string[] d = line.Split(new char[] { '\t', ',', '{', '}' }, StringSplitOptions.RemoveEmptyEntries);
            uint[] features = new uint[d.Length - 1];
            for (int i = 1; i < d.Length; i++) { features[i - 1] = UInt32.Parse(d[i]); }

            this.SetHash(d[0]);
            Array.Sort(features);
            this.m_SigData = features;
        }
        public override float Sum_Weights() { return this.m_SigData.Length; } //Uniform weighting
        public override float Get_Coeff_Weight(uint coeff) { return 1f; }

        public override string ToString()
        {
            return "MD5 " + this.HashString + " -> " + this.SigData.Length;
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

            ushort siglength = r.ReadUInt16();
            this.m_SigData = new uint[siglength];
            for (int i = 0; i < siglength; i++)
            {
                this.m_SigData[i] = r.ReadUInt32();
            }
        }
    
    }
}
