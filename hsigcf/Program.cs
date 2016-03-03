using iqdb;
using System;
using System.Collections.Generic;
using System.IO;

namespace hsigcf
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0) return;
            string out_file = args[0] + "-f.hsigs";
            Console.WriteLine(out_file + " output file.");
            //read hsig files
            HashSet<string> fil = new HashSet<string>();
            HashSet<string> written = new HashSet<string>();
            foreach (string arg in args)
            {
                if (arg.EndsWith(".csv"))
                {
                    foreach (String hash in File.ReadAllLines(arg))
                    {
                        fil.Add(hash.Trim());
                    }
                }
            }
            fil.TrimExcess();
            Console.WriteLine(fil.Count + " hashes as whitelist.");

            BinaryWriter hw = new BinaryWriter(new FileStream(out_file, FileMode.Append));
            foreach (string arg in args)
            {
                //For hsig lists we look for matches in the database
                if (arg.EndsWith(".hsigs"))
                {
                    BinaryReader binaryreader = new BinaryReader(File.OpenRead(arg));
                    Console.WriteLine(arg + " opened to read.");
                    uint sigs = 0;
                    while (binaryreader.BaseStream.Position < binaryreader.BaseStream.Length)
                    {
                        DateTime start = DateTime.Now;

                        HaarSignature sig = new HaarSignature(binaryreader);
                        if (fil.Count > 0)
                        {
                            if (!fil.Contains(sig.HashString))
                            {
                                sigs++;
                                continue;
                            }
                        }
                        if (!written.Contains(sig.HashString))
                        {
                            sig.Serialize(hw);
                            written.Add(sig.HashString);
                        }
                        sigs++;
                    }
                    binaryreader.Close();
                }
            }
            hw.Close();
        }
    }
}
