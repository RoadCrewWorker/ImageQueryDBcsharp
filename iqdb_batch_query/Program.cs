using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using iqdb;
using System.Threading;

namespace iqdb_batch_query
{
    public class SimQuery
    {

        public static List<QueryResult> ParallelSMQuery(List<QuerySparseMatrix> qsms, Signature[] querys, float limit_coeff)
        {
            ConcurrentBag<QueryResult> b = new ConcurrentBag<QueryResult>();
            List<Thread> searchers = new List<Thread>();
            foreach (Signature query in querys)
            {
                SimQuery searcher = new SimQuery(qsms, query, b, limit_coeff);
                Thread searchthread = new Thread(new ThreadStart(searcher.ThreadRun));
                searchers.Add(searchthread);
                searchthread.Start();
            }
            foreach (Thread searchthread in searchers) searchthread.Join();

            List<QueryResult> results = new List<QueryResult>(b);
            results.Sort();
            return results;
        }

        List<QuerySparseMatrix> Qsms;
        Signature Query;
        ConcurrentBag<QueryResult> Bag;
        float limit_coefficientmatch;

        public SimQuery(List<QuerySparseMatrix> qsms, Signature query, ConcurrentBag<QueryResult> bag, float limit_coeff)
        {
            this.Qsms = qsms;
            this.Query = query;
            this.Bag = bag;
            this.limit_coefficientmatch = limit_coeff;
        }
        public void ThreadRun()
        {
            List<QueryResult> gridresults=new List<QueryResult>();
            foreach (QuerySparseMatrix qsm in Qsms)
            {
                gridresults.AddRange(qsm.ExecuteQuery(this.Query, this.limit_coefficientmatch));
            }
            gridresults.Sort();
            int limit = 40;
            for (int i = 0; i < limit && i < gridresults.Count; i++)
            {
                QueryResult r=gridresults[i];
                if (r.Hash != this.Query.HashString)
                {
                    r.Query = this.Query;
                    Bag.Add(r);
                }
                else
                {
                    limit++;
                }
            }
        }
    }

    class Program
    {
        static List<QuerySparseMatrix> qsms = new List<QuerySparseMatrix>();

        static void Main(string[] args)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
            DateTime ts = DateTime.Now;
            HaarSignature.InitializeCoefficientTranslation();
            Console.WriteLine((DateTime.Now - ts).ToString() + " initialized constants."); ts = DateTime.Now;

            if (args.Length < 3)
            {
                Console.WriteLine("Arguments missing. Usage: \"<output name> <md5> <cores> <limit> ... (hsm and hsigs paths)\".");
                Console.ReadLine();
                return;
            }

            string fname = args[0];
            string md5 = args[1];
            int cores = Int32.Parse(args[2]);
            float limit = float.Parse(args[3]);
            if (limit > 1f || limit < 0f) limit = 0.6f;
            string cdir = System.AppDomain.CurrentDomain.BaseDirectory;

            foreach (string arg in args)
            {
                //load Integer sparse matrices
                if (arg.EndsWith(".qsm"))
                {
                    Console.Write("load Integer sparse matrix " + arg + " ... ");
                    BinaryReader r = new BinaryReader(File.OpenRead(arg));
                    QuerySparseMatrix qsm = new IntegerQuerySparseMatrix();
                    qsm.Deserialize(r);
                    r.Close();

                    Console.WriteLine(" complete.");
                    GC.Collect();
                    qsms.Add(qsm);
                }
                //load haar sparse matrices
                if (arg.EndsWith(".hsm"))
                {
                    Console.Write("load haar sparse matrix " + arg + " ... ");
                    BinaryReader r = new BinaryReader(File.OpenRead(arg));
                    HaarQuerySparseMatrix qsm = new HaarQuerySparseMatrix();
                    qsm.Deserialize(r);
                    r.Close();
                    //qsm.file_data = new FileInfo(arg);
                    Console.WriteLine(" complete.");
                    GC.Collect();
                    qsms.Add(qsm);
                }
            }
            Console.WriteLine((DateTime.Now - ts).ToString() + " loaded sparse search matrices."); ts = DateTime.Now;

            foreach (string arg in args)
            {
                if (arg.EndsWith(".hsigs"))
                {
                    DateTime hsig_start = DateTime.Now;
                    long start_position=0;

                    BinaryReader binaryreader = new BinaryReader(File.OpenRead(arg));
                    Console.WriteLine(arg + " opened to read.");
                    string outdir = Path.GetDirectoryName(arg);
                    string fn_output = (outdir == "" ? cdir : outdir) + "/" + fname + "_rel.csv";
                    Console.WriteLine("Output file: "+ fn_output);
                    StreamWriter w = new StreamWriter(File.Open(fn_output, FileMode.Append));

                    Console.Write("Skip to md5: "+md5);
                    md5 = (md5 == "-" ? null : md5);

                    uint sigs = 0;
                    Signature[] sigbatch = new Signature[cores];
                    uint queuedsigs = 0;
                    long total_len=binaryreader.BaseStream.Length;
                    while (binaryreader.BaseStream.Position < total_len) //TODO: make sure the last few signatures (when the batch doesnt fill up before the stream ends) are queried too!
                    {
                        HaarSignature sig = new HaarSignature(binaryreader);
                        if (md5 != null)
                        {
                            if (sig.HashString == md5)
                            {
                                md5 = null;
                                start_position = binaryreader.BaseStream.Position;
                            }
                            sigs++;
                            continue;
                        }

                        if (queuedsigs < cores)
                        {
                            sigbatch[queuedsigs] = sig;
                            queuedsigs++;
                            //Console.WriteLine("Queued sig " + sig.HashString + ", now " + queuedsigs);
                        }
                        else
                        {
                            DateTime start = DateTime.Now;
                            List<QueryResult> results = SimQuery.ParallelSMQuery(qsms, sigbatch, limit);
                            queuedsigs = 0;
                            foreach (QueryResult result in results)
                                w.WriteLine(result.ToCSV());

                            long c_pos = binaryreader.BaseStream.Position;
                            long processed_len = c_pos - start_position;
                            long remaining_len = total_len - c_pos;
                            TimeSpan elapsed=(DateTime.Now - hsig_start);
                            TimeSpan remaining=TimeSpan.FromTicks((long)(elapsed.Ticks*((double)remaining_len/(double)processed_len)));

                            Console.WriteLine("[" + Math.Round(100f * c_pos / total_len, 3) + "% " + (int)(c_pos / 1024) + "/" + (int)(total_len / 1024) + " KB, Elapsed: " + elapsed.ToString(@"d\.hh\:mm\:ss") + ", Remaining: " + remaining.ToString(@"d\.hh\:mm\:ss") + "] -> " + sigs + ":\t" + results.Count + " in " + (DateTime.Now - start).ToString(@"mm\:ss\.fff"));

                            if (results.Count > 0)
                            {
                                w.Flush();
                                GC.Collect();
                            }
                        }

                        sigs++;
                    }
                    w.Close();
                    binaryreader.Close();
                }
            }
        }
    }
}
