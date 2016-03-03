using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using iqdb;

namespace iqdb_import
{
    class Program
    {

        static void Main(string[] args)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
            HaarSignature.InitializeCoefficientTranslation();

            foreach (string arg in args)
            {
                //Generate Signatures from lists:
                //Haar signatures: list of maximum
                if (arg.EndsWith(".hcsv")) CSVtoSignatures(new string[] { arg }, arg + ".hsigs", 1);
                else if (arg.EndsWith(".hsigs")) BlockFileHsigs(arg, arg + "-bl");
                //Color signatures (including hsv)
                else if (arg.EndsWith(".ccsv")) CSVtoSignatures(new string[] { arg }, arg + ".csigs", 2);
                //Integer signatures: bag of ids
                else if (arg.EndsWith(".icsv")) CSVtoSignatures(new string[] { arg }, arg + ".isigs", 3);
                //Histogramms
                else if (arg.EndsWith(".rgbcsv")) CSVtoSignatures(new string[] { arg }, arg + ".rgbsigs", 4);
                else if (arg.EndsWith(".rgbsigs")) DumpRGBSigs(arg, arg + ".csv");
                //Transposed?
            }
            Console.WriteLine("Done. Exiting now.");
            //Console.ReadLine();
        }




        static void CSVtoSignatures(string[] source_files, string out_file, int type)
        {
            DateTime ts = DateTime.Now;
            BinaryWriter w = new BinaryWriter(new FileStream(out_file, FileMode.Append));

            //
            foreach (string source_file in source_files)
            {
                Console.WriteLine("Reading " + source_file); uint skip = 0; // UInt32.Parse(Console.ReadLine());
                FileStream fs = File.OpenRead(source_file);
                StreamReader sr = new StreamReader(fs);
                List<Signature> sigs = new List<Signature>();
                uint i = 0;
                while (!sr.EndOfStream)
                {
                    string l = sr.ReadLine();
                    i++;
                    if (i < skip) continue;

                    Signature s = null;
                    try
                    {
                        if (type == 1)
                        {
                            string[] p = l.Split('|');

                            string md5 = p[0].Trim().ToLower();
                            string file = p[1].Trim().ToLower();
                            if (File.Exists(file))
                            {
                                s = HaarSignature.FromFileName(md5, file);
                            }
                            else
                            {
                                Console.WriteLine("[" + i + ", " + sr.BaseStream.Position + "/" + sr.BaseStream.Length + "] File missing: " + file);
                            }
                        }
                        if (type == 2)
                        {
                            string[] p = l.Split('|');

                            string md5 = p[0].Trim().ToLower();
                            string file = p[1].Trim().ToLower();
                            if (File.Exists(file))
                            {
                                s = ColorSignature.FromFileName(md5, file);
                            }
                            else
                            {
                                Console.WriteLine("[" + i + ", " + sr.BaseStream.Position + "/" + sr.BaseStream.Length + "] File missing: " + file);
                            }
                        }
                        if (type == 4)
                        {
                            string[] p = l.Split('|');

                            string md5 = p[0].Trim().ToLower();
                            string file = p[1].Trim().ToLower();
                            if (File.Exists(file))
                            {
                                s = RGBSignature.FromFileName(md5, file);
                            }
                            else
                            {
                                Console.WriteLine("[" + i + ", " + sr.BaseStream.Position + "/" + sr.BaseStream.Length + "] File missing: " + file);
                            }
                        }
                        if (type == 3)
                        {
                            s = new IntegerSignature(l);
                        }
                        if (s != null)
                        {
                            sigs.Add(s);
                        }
                    }
                    catch (Exception x)
                    {
                        Console.WriteLine(i + " : " + x.ToString());
                    }

                    if (i % 10000 == 0)
                    {
                        Console.Write((sr.BaseStream.Position * 100 / sr.BaseStream.Length) + "% [" + i + ", " + sr.BaseStream.Position + "/" + sr.BaseStream.Length + "] " + (DateTime.Now - ts).ToString()); //+"Last:"+(s!=null?(BitConverter.ToString(s.Hash).Replace("-", "").ToLower() + '|' + ((RGBSignature)s).GetHexData()):"")
                        foreach (Signature sig in sigs)
                            sig.Serialize(w);
                        w.Flush();
                        sigs.Clear();
                        Console.WriteLine(" flushed.");
                        ts = DateTime.Now;
                    }
                }
                sr.Close();
                fs.Close();
            }
            w.Close();
        }

        static void DumpRGBSigs(string f_input, string csv_output)
        {
            Console.WriteLine("Reading " + f_input + " (" + ((new FileInfo(f_input)).Length / 1024) + " Kb)");
            BinaryReader binaryreader = new BinaryReader(File.OpenRead(f_input));
            StreamWriter textwriter = new StreamWriter(csv_output);
            while (binaryreader.BaseStream.Position < binaryreader.BaseStream.Length)
            {
                try
                {
                    RGBSignature sig = new RGBSignature(binaryreader);
                    textwriter.WriteLine(BitConverter.ToString(sig.Hash).Replace("-", "").ToLower() + '|' + sig.GetHexData());
                }
                catch (Exception e)
                {
                    Console.WriteLine(binaryreader.BaseStream.Position + "/" + binaryreader.BaseStream.Length + ": " + e.ToString());
                    break;
                }
            }
            textwriter.Close();
        }

        static void BlockFileHsigs(string f_input, string blockfilename)
        {
            Console.WriteLine("Reading " + f_input + " (" + ((new FileInfo(f_input)).Length / 1024) + " Kb)");
            BinaryReader binaryreader = new BinaryReader(File.OpenRead(f_input));
            BinaryWriter w_head = new BinaryWriter(new FileStream(blockfilename + ".head", FileMode.Append));
            BinaryWriter w_sigy = new BinaryWriter(new FileStream(blockfilename + ".ydat", FileMode.Append));
            BinaryWriter w_sigi = new BinaryWriter(new FileStream(blockfilename + ".idat", FileMode.Append));
            BinaryWriter w_sigq = new BinaryWriter(new FileStream(blockfilename + ".qdat", FileMode.Append));
            StreamWriter textwriter = new StreamWriter(blockfilename + ".head.csv");
            while (binaryreader.BaseStream.Position < binaryreader.BaseStream.Length)
            {
                HaarSignature s = new HaarSignature(binaryreader);
                textwriter.WriteLine(BitConverter.ToString(s.Hash).Replace("-", "").ToLower() + '|' + s.Sum_Weights());
                w_head.Write(s.Hash); //16 byte
                for (int i = 0; i < s.AverageLuminance.Length; i++) //4 byte * 3=12 byte
                {
                    w_head.Write(s.AverageLuminance[i]);
                }

                for (int i = 0; i < s.SigData.Length; i++) //30*4 byte= 120 byte
                {
                    uint channel = s.SigData[i] >> (HaarSignature.PIXEL_BITS + 1);
                    ushort coeff = (ushort)(s.SigData[i] % HaarSignature.CHANNEL_SIZE);
                    if (channel == 0) w_sigy.Write(coeff);
                    else if (channel == 1) w_sigi.Write(coeff);
                    else if (channel == 2) w_sigq.Write(coeff);
                }
            }
            w_head.Close();
            w_sigy.Close();
            w_sigi.Close();
            w_sigq.Close();
            textwriter.Close();
        }
    }
}
