using iqdb;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace hsig2hsm
{
    class Program
    {
        static void Main(string[] args)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
            HaarSignature.InitializeCoefficientTranslation();

            foreach (string arg in args)
            {
                if (arg.EndsWith(".hsigs"))
                {
                    GenerateSparseMatrix(new string[] { arg }, arg + "-h", 1);
                }
            }
        }

        static void GenerateSparseMatrix(string[] source_files, string out_file, int type)
        {
            //Console.WriteLine("Reading " + source_files.Length + " files, split every (0 disables):");
            uint split = 4000000; // UInt32.Parse(Console.ReadLine());
            //if (split == 0) split = UInt32.MaxValue;

            List<Signature> signatures = new List<Signature>();
            HashSet<string> hashes = new HashSet<string>();
            int c = 1;
            int f = 0;
            foreach (string source_file in source_files)
            {
                Console.WriteLine("Reading " + source_file + " (" + ((new FileInfo(source_file)).Length / 1024) + " Kb)");
                BinaryReader binaryreader = new BinaryReader(File.OpenRead(source_file));
                while (binaryreader.BaseStream.Position < binaryreader.BaseStream.Length)
                {
                    try
                    {
                        Signature s = null;
                        if (type == 1) s = new HaarSignature(binaryreader);
                        else if (type == 2) s = new ColorSignature(binaryreader);
                        else if (type == 3) s = new IntegerSignature(binaryreader);
                        else if (type == 4) s = new RGBSignature(binaryreader);

                        if (s != null)
                        {
                            if (hashes.Contains(s.HashString))
                            {
                                Console.WriteLine(s.HashString + " already deserialized...");
                            }
                            else
                            {
                                hashes.Add(s.HashString);
                                signatures.Add(s);
                            }
                        }

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(binaryreader.BaseStream.Position + "/" + binaryreader.BaseStream.Length + ": " + c + " -> " + e.ToString());
                        break;
                    }

                    if (c < split)
                    {
                        c++;
                    }
                    else
                    {
                        c = 1;
                        QuerySparseMatrix qsm = null;
                        Console.WriteLine("Creating QuerySparseMatrix: " + signatures.Count + " Signatures.");
                        //qsm = IntegerQuerySparseMatrix.FromSignatures(signatures); //WARNING: looses color, avg info
                        qsm = HaarQuerySparseMatrix.FromHaarSignatures(signatures);
                        GC.Collect();
                        if (qsm != null)
                        {
                            string cname = out_file + "-" + f + ".hsm";
                            Console.WriteLine("Storing querygrid: " + cname);
                            BinaryWriter binarywriter = new BinaryWriter(new FileStream(cname, FileMode.Create));
                            qsm.Serialize(binarywriter);
                            binarywriter.Close();
                            signatures.Clear();
                            f++;
                        }

                    }
                }
                binaryreader.Close();
                GC.Collect();
                Console.WriteLine("Now " + signatures.Count + " Signatures.");
            }

            if (signatures.Count > 0)
            {
                QuerySparseMatrix qsm = null;
                Console.WriteLine("Creating QuerySparseMatrix: " + signatures.Count + " Signatures.");
                //qsm = IntegerQuerySparseMatrix.FromSignatures(signatures); //WARNING: looses color, avg info
                qsm = HaarQuerySparseMatrix.FromHaarSignatures(signatures);
                GC.Collect();
                if (qsm != null)
                {
                    string cname = out_file + "-" + f + ".hsm";
                    Console.WriteLine("Storing querygrid: " + cname);
                    BinaryWriter binarywriter = new BinaryWriter(new FileStream(cname, FileMode.Create));
                    qsm.Serialize(binarywriter);
                    binarywriter.Close();
                    signatures.Clear();
                    f++;
                }
            }
        }

    }
}
