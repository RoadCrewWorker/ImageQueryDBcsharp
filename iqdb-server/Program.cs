using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using iqdb;

namespace iqdb_server
{
    public class SMQuery
    {
        public static List<QueryResult> ParallelSMQuery(List<QuerySparseMatrix> qsms, Signature query, float limit_coeff)
        {
            ConcurrentBag<QueryResult> b = new ConcurrentBag<QueryResult>();
            List<Thread> searchers = new List<Thread>();
            foreach(QuerySparseMatrix current_querygrid in qsms)
            {
                SMQuery searcher = new SMQuery(current_querygrid, query, b, limit_coeff);
                Thread searchthread = new Thread(new ThreadStart(searcher.ThreadRun));
                searchers.Add(searchthread);
                searchthread.Start();
            }
            foreach (Thread searchthread in searchers) searchthread.Join();

            List<QueryResult> results = new List<QueryResult>(b);
            results.Sort();
            return results;
        }

        QuerySparseMatrix qsm;
        Signature Query;
        float limit_coefficientmatch;
        ConcurrentBag<QueryResult> Bag;

        public SMQuery(QuerySparseMatrix grid, Signature query, ConcurrentBag<QueryResult> bag, float limit_coeff)
        {
            this.qsm = grid;
            this.Query = query;
            this.Bag = bag;
            this.limit_coefficientmatch = limit_coeff;
        }
        public void ThreadRun()
        {
            List<QueryResult> gridresults = qsm.ExecuteQuery(this.Query, this.limit_coefficientmatch);
            foreach (QueryResult result in gridresults)
            {
                result.Query = this.Query;
                Bag.Add(result);
            }
        }
    }


    class Program
    {
        static List<QuerySparseMatrix> qsms = new List<QuerySparseMatrix>();

        static void Main(string[] args)
        {
            //1. Initialize Data structures
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
            DateTime ts = DateTime.Now;
            HaarSignature.InitializeCoefficientTranslation();
            Console.WriteLine((DateTime.Now - ts).ToString() + " initialized constants."); ts = DateTime.Now;
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
            Console.WriteLine((DateTime.Now - ts).ToString() + " loaded database."); ts = DateTime.Now;

            WebServer server_hsm = new WebServer(RespondHSMQuery, "http://*:6669/ciq/");
            server_hsm.Run();

            string cmd = "";
            while ((cmd = Console.ReadLine()) != "quit")
            {
                if (cmd == "hsmdump")
                {
                    StreamWriter textwriter = new StreamWriter("hsmhashes.csv");
                    foreach (HaarQuerySparseMatrix hqsm in qsms)
                    {
                        foreach (HaarMiniSig sig in hqsm.Signatures)
                        {
                            textwriter.WriteLine(BitConverter.ToString(sig.Hash).Replace("-", "").ToLower() + '|' + sig.TotalWeight);
                        }
                    }
                    textwriter.Close();
                }
            }
            server_hsm.Stop();
        }

        public static string RespondHSMQuery(HttpListenerRequest request)
        {
            log("New connection: " + request.RawUrl);
            string source_image = request.QueryString["url"];
            float limit_coeff = 0.4f;
            if (!Single.TryParse(request.QueryString["lc"], out limit_coeff)) limit_coeff = 0.4f;
            float limit_eavg = 0.4f;
            if (!Single.TryParse(request.QueryString["le"], out limit_eavg)) limit_eavg = 0.4f;

            try
            {
                //Obtain image
                DateTime ts = DateTime.Now;
                WebClient cl = new WebClient();
                byte[] imgd = cl.DownloadData(source_image);
                cl.Dispose();
                MemoryStream ms = new MemoryStream(imgd);
                Image i = Image.FromStream(ms);
                Signature query = HaarSignature.FromImage(null, i); //ColorSignature.FromImage(null, i); //
                i.Dispose();
                ms.Dispose();

                //Query our Database grid
                List<QueryResult> results = SMQuery.ParallelSMQuery(qsms, query, limit_coeff);

                log("<< " + (DateTime.Now - ts).ToString() + " | " + results.Count + " results > " + limit_coeff + " | "+limit_eavg + " from: " + source_image );
                uint result_limit = 50;
                //Format response packet and send.
                if (results.Count == 0) return "[]";

                StringBuilder result_json = new StringBuilder();
                int l = 0;
                foreach (QueryResult query_result in results)
                {
                    query_result.Query = query;
                    if (l++ > result_limit)
                        break;
                    result_json.Append(',').Append(query_result.ToJSON());
                }
                return "[" + result_json.ToString().Substring(1) + "]";
            }
            catch (Exception e)
            {
                log(source_image + ": " + e.ToString());
                return "[]";
            }
        }

        

        static void log(string message)
        {
            Console.WriteLine(DateTime.Now.ToLongTimeString() + " " + message);
        }
    }
}
