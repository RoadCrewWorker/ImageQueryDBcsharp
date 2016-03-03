using System;
using System.Collections.Generic;
using System.IO;

namespace iqdb
{
    public struct HaarMiniSig
    {
        public byte[] Hash;
        public float TotalWeight;
        public float[] YUV;
    }

    public class DiskHaarQuerySparseMatrix : QuerySparseMatrix
    {
        //Signature storage
        public HaarMiniSig[] Signatures;
        private uint[][] col_ids;
        public FileInfo file_data;
        private long fileoffset_column_ids, fileoffset_end;

        public static DiskHaarQuerySparseMatrix FromHaarSignatures(List<Signature> sigs)
        {
            DiskHaarQuerySparseMatrix qsm = new DiskHaarQuerySparseMatrix();
            qsm.Signatures = new HaarMiniSig[sigs.Count];

            List<uint>[] sigids_for = new List<uint>[HaarSignature.CHANNEL_COUNT * HaarSignature.CHANNEL_SIZE];
            for (uint sigid = 0; sigid < sigs.Count; sigid++)
            {
                //Assign array index as unique id
                HaarSignature sig = (HaarSignature)sigs[(int)sigid];
                sig.ID = sigid;

                //Mirror meta information
                HaarMiniSig s = new HaarMiniSig();
                s.YUV = sig.AverageLuminance;
                s.Hash = sig.Hash;
                s.TotalWeight = sig.Sum_Weights();
                qsm.Signatures[sigid] = s;

                //Distribute coefficients into ids
                foreach (uint coeff in sig.SigData)
                {
                    (sigids_for[coeff] == null ? (sigids_for[coeff] = new List<uint>()) : sigids_for[coeff]).Add(sigid);
                }
            }

            int coeff_max = HaarSignature.CHANNEL_COUNT * HaarSignature.CHANNEL_SIZE;
            int c = sigs.Count;

            qsm.row_id_offsets = new uint[coeff_max + 1];

            //Count number of entries
            int nzelements = 0;
            foreach (List<uint> sigids_list in sigids_for)
            {
                nzelements += (sigids_list == null ? 0 : sigids_list.Count);
            }
            qsm.column_ids = new uint[nzelements];

            //Flatten sigids_for into static double array
            uint element_pointer = 0;
            for (int coeff = 0; coeff < coeff_max; coeff++)
            {
                List<uint> sigids_for_coeff = sigids_for[coeff];
                qsm.row_id_offsets[coeff] = element_pointer;
                if (sigids_for_coeff == null)
                {
                    continue;
                }
                foreach (uint sigid in sigids_for_coeff)
                {
                    qsm.column_ids[element_pointer] = sigid;
                    element_pointer++;
                }
            }
            qsm.row_id_offsets[qsm.row_id_offsets.Length - 1] = (uint)nzelements;
            return qsm;
        }

        public override void Serialize(BinaryWriter w)
        {
            //Can not be serialized
            throw new NotSupportedException("DiskHaarQuerySparseMatrix is read only.");
        }

        public override void Deserialize(BinaryReader r)
        {
            //Load in memory: 
            /*
             * 
             * */
            uint t = r.ReadUInt32();
            this.Signatures = new HaarMiniSig[t];
            for (int i = 0; i < t; i++)
            {
                HaarMiniSig s = new HaarMiniSig();
                s.Hash = r.ReadBytes(16);
                s.TotalWeight = r.ReadSingle();
                s.YUV = new float[HaarSignature.CHANNEL_COUNT];
                for (int c = 0; c < HaarSignature.CHANNEL_COUNT; c++)
                {
                    s.YUV[c] = r.ReadSingle();
                }
                this.Signatures[i] = s;
            }

            int maxsid = HaarSignature.CHANNEL_COUNT * HaarSignature.CHANNEL_SIZE;
            this.row_id_offsets = new uint[maxsid];
            this.col_ids = new uint[maxsid][];

            for (int i = 0; i < maxsid; i++)
            {
                row_id_offsets[i] = r.ReadUInt32();
            }

            uint gl = r.ReadUInt32();
            this.fileoffset_column_ids = r.BaseStream.Position;
            this.fileoffset_end = r.BaseStream.Length;
            this.column_ids = null;
        }

        private uint[] ReadColIDs(BinaryReader r, uint coeff)
        {
            uint row_offset = this.row_id_offsets[coeff],
                row_end = (coeff + 1 == this.row_id_offsets.Length) ? (uint)this.column_ids.Length : this.row_id_offsets[coeff + 1];
            //For too large groups skip
            /*
            if ((row_end - row_offset)*2 > this.Signatures.Length)
            {
                return new uint[0]; 
            }
             * */
            uint[] ids = new uint[row_end - row_offset];
            long streamoffset = this.fileoffset_column_ids + (row_offset * sizeof(UInt32));
            r.BaseStream.Seek(streamoffset, SeekOrigin.Begin);
            uint i = 0;
            while (row_offset < row_end) //distribute matched coefficients weights
            {
                ids[i] = r.ReadUInt32();
                row_offset++;
                i++;
            }
            return ids;
        }

        public override List<QueryResult> ExecuteQuery(Signature query, float limit_coefficients)
        {
            if (!(query is HaarSignature)) throw new InvalidDataException("Query signature is not proper HaarSignature type.");
            HaarSignature hquery = (HaarSignature)query;
            float weight_total = 0;
            if (HaarSignature.CHANNEL_COUNT != hquery.AverageLuminance.Length)
            {
                //error;
            }

            DateTime ts = DateTime.Now;
            //YUV comparison
            float[] yuv_distance = new float[this.Signatures.Length];
            for (int channel = 0; channel < HaarSignature.CHANNEL_COUNT; channel++)
            {
                float avglum_query = hquery.AverageLuminance[channel];
                for (int i = 0; i < this.Signatures.Length; i++)
                {
                    yuv_distance[i] += HaarSignature.Weights[1, 0, channel] * Math.Abs(avglum_query - this.Signatures[i].YUV[channel]);
                }
            }

            //Coefficient Match
            float[] w_matches = new float[this.Signatures.Length];
            foreach (uint coeff in query.SigData)
            {
                float weight_coeff = HaarSignature.WeightLookUp[coeff >> 1]; //right shift removes sign bit
                
                uint[] ids = this.col_ids[coeff];
                if(this.col_ids[coeff]==null){
                    //Load 
                    ids = this.col_ids[coeff] = ReadColIDs(new BinaryReader(file_data.OpenRead()), coeff);
                }
                if (ids.Length > 0)
                {
                    weight_total += weight_coeff;
                }
                foreach(uint id in ids){
                    w_matches[id]+=weight_coeff;
                }
            }

            //Result compilation
            List<QueryResult> v = new List<QueryResult>();

            float queryTotalWeight = query.Sum_Weights();
            if (w_matches.Length != yuv_distance.Length || yuv_distance.Length != this.Signatures.Length)
            {
                return v;
            }

            for (uint i = 0; i < this.Signatures.Length; i++)
            {
                float
                    metric_value = (w_matches[i]),
                    metric_max = queryTotalWeight + this.Signatures[i].TotalWeight - (w_matches[i]),
                    metric_percent = metric_value / metric_max;
                if (metric_percent > limit_coefficients && yuv_distance[i] < limit_coefficients)
                {
                    v.Add(new HaarQueryResult(BitConverter.ToString(this.Signatures[i].Hash).Replace("-", "").ToLower(), yuv_distance[i], metric_value, metric_max));
                }
            }
            return v;
        }

    }
}
