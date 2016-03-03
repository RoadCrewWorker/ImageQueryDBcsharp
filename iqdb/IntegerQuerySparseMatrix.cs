using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace iqdb
{
    public struct IntMiniSig
    {
        public byte[] Hash;
        public float TotalWeight;
    }

    public class IntegerQuerySparseMatrix : QuerySparseMatrix
    {
        public IntMiniSig[] Signatures;

        public static IntegerQuerySparseMatrix FromSignatures(List<Signature> sigs)
        {
            IntegerQuerySparseMatrix qsm = new IntegerQuerySparseMatrix();
            qsm.Signatures = new IntMiniSig[sigs.Count];

            uint max_coeff = 0;
            for (uint sigid = 0; sigid < sigs.Count; sigid++)
            {
                //Assign array index as unique id
                Signature sig = sigs[(int)sigid];
                sig.ID = sigid;

                //Mirror meta information
                IntMiniSig s = new IntMiniSig();
                s.Hash = sig.Hash;
                s.TotalWeight = sig.Sum_Weights();

                qsm.Signatures[sigid] = s;
                max_coeff = Math.Max(max_coeff, sig.SigData.Max<uint>());
            }

            List<uint>[] temp = new List<uint>[(int)max_coeff + 1];
            for (uint sigid = 0; sigid < sigs.Count; sigid++)
            {
                //Distribute coefficients into ids
                foreach (uint coeff in sigs[(int)sigid].SigData)
                {
                    if (temp[coeff] == null) temp[coeff] = new List<uint>();
                    temp[coeff].Add(sigid);
                }
            }
            int c = sigs.Count;
            qsm.row_id_offsets = new uint[max_coeff + 1];

            //Count number of entries
            uint nzelements = 0;
            foreach (List<uint> sigids_list in temp)
            {
                if (sigids_list != null) nzelements += (uint)sigids_list.Count;
            }
            qsm.column_ids = new uint[nzelements];

            //Flatten sigids_for into static double array
            uint element_pointer = 0;
            for (uint coeff = 0; coeff < max_coeff; coeff++)
            {
                qsm.row_id_offsets[coeff] = element_pointer;

                if (temp[coeff] == null)
                {
                    continue;
                }
                foreach (uint sigid in temp[coeff])
                {
                    qsm.column_ids[element_pointer] = sigid;
                    element_pointer++;
                }
            }
            qsm.row_id_offsets[qsm.row_id_offsets.Length - 1] = nzelements;
            return qsm;
        }

        public override void Serialize(BinaryWriter w)
        {
            uint t = (uint)Signatures.Length;
            w.Write(t);
            for (int i = 0; i < t; i++)
            {
                w.Write(this.Signatures[i].Hash);
                w.Write(this.Signatures[i].TotalWeight);
            }

            w.Write(this.row_id_offsets.Length);
            for (int i = 0; i < this.row_id_offsets.Length; i++)
            {
                w.Write(this.row_id_offsets[i]);
            }

            w.Write(this.column_ids.Length);
            for (int i = 0; i < this.column_ids.Length; i++)
            {
                w.Write(this.column_ids[i]);
            }
        }

        public override void Deserialize(BinaryReader r)
        {
            uint t = r.ReadUInt32();
            this.Signatures = new IntMiniSig[t];
            for (int i = 0; i < t; i++)
            {
                IntMiniSig s = new IntMiniSig();
                s.Hash = r.ReadBytes(16);
                s.TotalWeight = r.ReadSingle();
                this.Signatures[i] = s;
            }

            uint maxsid = r.ReadUInt32();
            this.row_id_offsets = new uint[maxsid];
            for (int i = 0; i < maxsid; i++)
            {
                row_id_offsets[i] = r.ReadUInt32();
            }

            uint gl = r.ReadUInt32();

            this.column_ids = new uint[gl];
            for (int i = 0; i < gl; i++)
            {
                this.column_ids[i] = r.ReadUInt32();
            }
        }

        public override List<QueryResult> ExecuteQuery(Signature query, float limit_coefficients)
        {
            float weight_total = 0;

            //Coefficient Match
            float[] w_matches = new float[this.Signatures.Length];
            foreach (uint coeff in query.SigData)
            {
                float coeff_weight = query.Get_Coeff_Weight(coeff);
                weight_total += coeff_weight;
                uint row_end = (coeff + 1 == this.row_id_offsets.Length) ? (uint)this.column_ids.Length : this.row_id_offsets[coeff + 1];
                for (uint row_offset = this.row_id_offsets[coeff]; row_offset < row_end; row_offset++) //distribute matched coefficients weights
                {
                    w_matches[this.column_ids[row_offset]] += coeff_weight;
                }
            }

            //Result compilation
            List<QueryResult> v = new List<QueryResult>();

            float queryTotalWeight = query.Sum_Weights();

            for (uint i = 0; i < this.Signatures.Length; i++)
            {
                float
                    metric_value = (w_matches[i]),
                    metric_max = queryTotalWeight + this.Signatures[i].TotalWeight - (w_matches[i]);
                if (metric_value / metric_max > limit_coefficients)
                {
                    v.Add(new IntegerQueryResult(BitConverter.ToString(this.Signatures[i].Hash).Replace("-", "").ToLower(), metric_value, metric_max));
                }
            }
            return v;
        }
    }
}
