using System;
using System.Collections.Generic;
using System.IO;

namespace idqb
{
    

    public class QueryGrid
    {
        private float[][] avgls; // Contains Signature avg components. Size: 3 * Sig.Count
        private float[] weightsums; //Contains sum of weights for each sig coefficients
        private byte[][] index2sighash; //Maps internal index values to imgids. Size: Sig.Count

        //these 2 arrays construct a sparse matrix where each value is implied to be 1
        private uint[] sigid2groupoffset; //Offset into groups array, 0 means null.
        private uint[] groups; //Stores indexes. MaxSize: Sig.Count*Sig.CoeffsCount+n

        public static QueryGrid FromSignatures(int max_coefficient, List<HaarSignature> sigs)
        {
            Dictionary<uint, Signature> sigdict=new Dictionary<uint,Signature>();
            List<uint>[] grid = new List<uint>[max_coefficient];

            foreach (HaarSignature sig in sigs)
            {
                uint sigid = (uint)sigdict.Count;
                if (sig.ID != 0 && sig.ID < sigid) continue;

                sig.ID = sigid;
                sigdict.Add(sigid, sig);
                foreach (uint coeff in sig.SigData)
                {
                    if (coeff >= grid.Length)
                    {
                        throw new InvalidDataException("Invalid sig " + coeff + " in " + sig.ToString());
                    }
                    if (grid[coeff] == null)
                    {
                        grid[coeff] = new List<uint>();
                    }
                    grid[coeff].Add(sigid);
                }
            }

            QueryGrid qg = new QueryGrid();
            Dictionary<uint, uint> imgid2index = new Dictionary<uint, uint>();
            int num_color = 3;
            int maxsid = num_color * HaarSignature.CHANNEL_SIZE;
            int c = sigdict.Count;
            uint index = 0;

            qg.sigid2groupoffset = new uint[maxsid];
            qg.index2sighash = new byte[c][];
            qg.avgls = new float[num_color][];
            for (int i = 0; i < num_color; i++)
            {
                qg.avgls[i] = new float[c];
            }
            qg.weightsums = new float[c];

            foreach (KeyValuePair<uint, Signature> s in sigdict)
            {
                Signature sig = s.Value;
                qg.index2sighash[index] = sig.Hash;
                //this.sigs[index] = s.Value;
                imgid2index.Add(sig.ID, index);
                qg.weightsums[index] = sig.Sum_Weights();
                for (int i = 0; i < num_color; i++) { qg.avgls[i][index] = ((HaarSignature)sig).AverageLuminance[i]; }
                index++;
            }
            int common_limit = c / 10, gpointer = 0;
            uint glistc = 1, glmax = 1;
            for (int sid = 0; sid < maxsid; sid++)
            {
                List<uint> n = grid[sid];
                if (n != null) { glmax += (uint)n.Count + 1; }
            }
            qg.groups = new uint[glmax];
            qg.groups[gpointer++] = 0; //First zero value= empty default group with length 0

            for (int sid = 0; sid < maxsid; sid++)
            {
                List<uint> n = grid[sid];
                if (n == null)
                {
                    continue;
                }

                qg.sigid2groupoffset[sid] = glistc;
                uint ncount = (uint)n.Count;
                qg.groups[gpointer++] = ncount;
                glistc += ncount + 1;
                foreach (uint imgid in n)
                {
                    qg.groups[gpointer++] = imgid2index[imgid];
                }
            }
            return qg;
        }

        public void Serialize(BinaryWriter w)
        {
            uint t = (uint)index2sighash.Length;
            w.Write(t);
            for (int i = 0; i < t; i++) w.Write(index2sighash[i]);
            for (int i = 0; i < t; i++) w.Write(weightsums[i]);

            byte num_color = (byte)this.avgls.Length;
            w.Write(num_color);
            for (int c = 0; c < num_color; c++)
                for (int i = 0; i < t; i++)
                    w.Write(avgls[c][i]);

            int maxsid = num_color * HaarSignature.CHANNEL_SIZE;
            for (int i = 0; i < maxsid; i++) w.Write(sigid2groupoffset[i]);

            w.Write(groups.Length);
            for (int i = 0; i < groups.Length; i++) w.Write(groups[i]);
        }

        public void Deserialize(BinaryReader r)
        {
            uint t = r.ReadUInt32();
            this.index2sighash = new byte[t][];
            this.weightsums = new float[t];
            for (int i = 0; i < t; i++) this.index2sighash[i] = r.ReadBytes(16);
            for (int i = 0; i < t; i++) this.weightsums[i] = r.ReadSingle();

            int num_color = r.ReadByte();
            this.avgls = new float[num_color][];
            for (int c = 0; c < num_color; c++)
            {
                this.avgls[c] = new float[t];
                for (int i = 0; i < t; i++)
                    this.avgls[c][i] = r.ReadSingle();
            }

            int maxsid = num_color * HaarSignature.CHANNEL_SIZE;
            this.sigid2groupoffset = new uint[maxsid];
            for (int i = 0; i < maxsid; i++) sigid2groupoffset[i] = r.ReadUInt32();

            uint gl = r.ReadUInt32();

            groups = new uint[gl];
            for (int i = 0; i < gl; i++) groups[i] = r.ReadUInt32();
        }

        public void Analyze()
        {
            uint onecount = 0, maxgroup = 0,groupcount = 0,max_cid = 0,legitgroups = 0,legitcx = 0,common_limit = (uint)this.index2sighash.Length / 10,over_limit = 0;
            for (uint i = 0; i < this.sigid2groupoffset.Length; i++)
            {
                uint go = this.sigid2groupoffset[i];
                uint gl = this.groups[go];
                if (gl > 0)
                {
                    groupcount++;
                    if (gl == 1) onecount++;
                    else if (gl > common_limit)
                    {
                        over_limit++;
                        if (gl > maxgroup)
                        {
                            maxgroup = gl;
                            max_cid = i;
                        }
                    }
                    else
                    {
                        legitgroups++;
                        legitcx += ((gl + 1) * gl / 2);
                    }
                }
            }
            Console.Write(
                "Total Groups: " + groupcount +
                " L=1: " + onecount +
                " L>" + common_limit + ": " + over_limit + " [" + Math.Round(100.0f * over_limit / groupcount, 2) + "%]" +
                " OK: " + legitgroups + " C=" + legitcx + " [" + Math.Round(100.0f * legitgroups / groupcount, 2) + "%]" +
                " max-L: " + max_cid + " x " + maxgroup + " [" + Math.Round(100.0f * maxgroup / legitcx, 2) + "%]"
                );
        }

        public void PruneForDuplicates()
        {
            //Drop: groups with 1 element.
            int count = this.index2sighash.Length / 10;
            int[] filtered = new int[3];
            for (int i = 0; i < this.sigid2groupoffset.Length; i++)
            {
                /*
                uint channel = HSignature.ict_wso[i >> 1] >> 14;
                if (channel > 0)
                {
                    this.sigid2groupoffset[i] = 0;
                    filtered[0]++;
                    continue;
                }
                 */
                /*
                float weight = HSignature.WeightLookUp[i >> 1];
                if (weight <= 0.3f)
                {
                    this.sigid2groupoffset[i] = 0;
                    filtered[1]++;
                    continue;
                }
                */
                uint g_itr = this.sigid2groupoffset[i];
                if (g_itr == 0) continue;

                uint g_count = this.groups[g_itr];
                if (g_count == 1 || g_count > count)
                {
                    this.sigid2groupoffset[i] = 0;
                    filtered[2]++;
                    continue;
                }
            }
            for (int i = 0; i < filtered.Length; i++)
            {
                Console.WriteLine(i + " " + filtered[i]);
            }
        }

        public List<QueryResult> ExecuteQuery(HaarSignature query, float limit_coefficients, bool verbose, float limit_error_averagepixel)
        {
            float weight_total = 0;
            int num_colors = query.AverageLuminance.Length;
            int database_size = this.index2sighash.Length;
            float w_querysum = query.Sum_Weights();

            DateTime ts = DateTime.Now;
            float[] error_averagepixel = new float[database_size];
            for (int channel = 0; channel < num_colors; channel++)
            {
                float avglum_query = query.AverageLuminance[channel];
                for (int i = 0; i < this.avgls[channel].Length; i++)
                    error_averagepixel[i] += HaarSignature.Weights[1, 0, channel] * Math.Abs(avglum_query - this.avgls[channel][i]);
            }

            float[] w_matches = new float[database_size];
            for (int current_coefficient = 0; current_coefficient < query.SigData.Length; current_coefficient++) //~100 loops
            {
                uint signature_coefficient = query.SigData[current_coefficient];
                float weight = HaarSignature.WeightLookUp[signature_coefficient >> 1]; //right shift removes sign bit
                uint group_iterator = this.sigid2groupoffset[signature_coefficient];
                if (group_iterator == 0) continue; //group has length and is empty
                uint group_count = this.groups[group_iterator++];
                weight_total += weight;
                /*if (10 * group_count > database_size)
                {
                    general_match += weight;
                }
                else
                {*/
                uint group_end = group_iterator + group_count;
                while (group_iterator < group_end) //distribute matched coefficients weights
                {
                    w_matches[this.groups[group_iterator++]] += weight;
                }
                //}
            }

            List<QueryResult> v = new List<QueryResult>();

            for (uint i = 0; i < database_size; i++)
            {
                float
                    metric_value = (w_matches[i]),
                    metric_max = w_querysum + this.weightsums[i] - (w_matches[i]),
                    metric_percent = metric_value / metric_max;
                if (metric_percent > limit_coefficients && error_averagepixel[i] < limit_error_averagepixel)
                {
                    v.Add(new QueryResult(BitConverter.ToString(this.index2sighash[i]).Replace("-", "").ToLower(), error_averagepixel[i], metric_value, metric_max));
                }
            }
            return v;
        }
    }
}
