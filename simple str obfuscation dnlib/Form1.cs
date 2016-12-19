using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace simple_str_obfuscation_dnlib
{
    public partial class Form1 : Form
    {

        public string DirectoryName = "";
        public static MethodDef init;
        public static int encodedSTR_amount;

        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Browse for target assembly";
            openFileDialog.InitialDirectory = "c:\\";
            if (DirectoryName != "")
            {
                openFileDialog.InitialDirectory = this.DirectoryName;
            }
            openFileDialog.Filter = "All files (*.exe,*.dll)|*.exe;*.dll";
            openFileDialog.FilterIndex = 2;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string fileName = openFileDialog.FileName;
                textBox1.Text = fileName;
                int num = fileName.LastIndexOf("\\", StringComparison.Ordinal);
                if (num != -1)
                {
                    DirectoryName = fileName.Remove(num, fileName.Length - num);
                }
                if (DirectoryName.Length == 2)
                {
                    DirectoryName += "\\";
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            try
            {
                //we load the file
                ModuleDefMD module = ModuleDefMD.Load(textBox1.Text);
                //lets inject the whole str dec/enc class
                InjectClass(module);


                //now let's go with every string in the file
                foreach (TypeDef type in module.GetTypes())
                {
                    //we dont want to obfuscate the <Module> class ;)
                    if (type.IsGlobalModuleType) continue;

                    foreach(MethodDef method in type.Methods)
                    {
                        //check if method has a method.Body
                        if (!method.HasBody) continue;
                        //lets go to the method now
                        var instr = method.Body.Instructions;
                        for (int i = 0; i < instr.Count - 3; i++)
                        {
                            if (instr[i].OpCode == OpCodes.Ldstr)
                            {
                                //get the original string
                                var originalSTR = instr[i].Operand as string;
                                //encode it
                                var encodedSTR = Encrypt(originalSTR);
                                //replace current LDSTR (string) with the ncoded one
                                instr[i].Operand = encodedSTR;

                                // 'i' = current instruction, we want to inject a CALL instruction
                                // right after it. init is the Decrypt function we injected before
                                instr.Insert(i + 1, Instruction.Create(OpCodes.Call, init));


                                encodedSTR_amount += 1;
                            }
                        }
                    }
                }

                string text2 = Path.GetDirectoryName(textBox1.Text);
                if (!text2.EndsWith("\\"))
                {
                    text2 += "\\";
                }
                string path = text2 + Path.GetFileNameWithoutExtension(textBox1.Text) + "_patched" +
                              Path.GetExtension(textBox1.Text);
                var opts = new ModuleWriterOptions(module);
                opts.Logger = DummyLogger.NoThrowInstance;
                module.Write(path, opts);
                label1.Text = "Successfully obfuscated " + encodedSTR_amount + " strings !";
            }
            catch (Exception ex)
            {
                //very simple error handling
                MessageBox.Show(ex.ToString());
            }
        }


        public static void InjectClass(ModuleDef module)
        {
            //We declare our Module, here we want to load the EncryptionHelper class
            ModuleDefMD typeModule = ModuleDefMD.Load(typeof(EncryptionHelper).Module);
            //We declare EncryptionHelper as a TypeDef using it's Metadata token (needed)
            TypeDef typeDef = typeModule.ResolveTypeDef(MDToken.ToRID(typeof(EncryptionHelper).MetadataToken));
            //We use confuserEX InjectHelper class to inject EncryptionHelper class into our target, under <Module>
            IEnumerable<IDnlibDef> members = InjectHelper.Inject(typeDef, module.GlobalType, module);
            //We find the Decrypt() Method in EncryptionHelper we just injected
            init = (MethodDef)members.Single(method => method.Name == "Decrypt");
            //we will call this method later

            //We just have to remove .ctor method because otherwise it will
            //lead to Global constructor error (e.g [MD]: Error: Global item (field,method) must be Static. [token:0x06000002] / [MD]: Error: Global constructor. [token:0x06000002] )
            foreach (MethodDef md in module.GlobalType.Methods)
            {
                if (md.Name == ".ctor")
                {
                    module.GlobalType.Remove(md);
                    //Now we go out of this mess
                    break;
                }
            }
        }

        //https://social.msdn.microsoft.com/Forums/vstudio/en-US/d6a2836a-d587-4068-8630-94f4fb2a2aeb/encrypt-and-decrypt-a-string-in-c?forum=csharpgeneral
        static readonly string PasswordHash = "P@@Sw0rd";
        static readonly string SaltKey = "S@LT&KEY";
        static readonly string VIKey = "@1B2c3D4e5F6g7H8";

        public static string Encrypt(string plainText)
        {
            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);

            byte[] keyBytes = new Rfc2898DeriveBytes(PasswordHash, Encoding.ASCII.GetBytes(SaltKey)).GetBytes(256 / 8);
            var symmetricKey = new RijndaelManaged() { Mode = CipherMode.CBC, Padding = PaddingMode.Zeros };
            var encryptor = symmetricKey.CreateEncryptor(keyBytes, Encoding.ASCII.GetBytes(VIKey));

            byte[] cipherTextBytes;

            using (var memoryStream = new MemoryStream())
            {
                using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                {
                    cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                    cryptoStream.FlushFinalBlock();
                    cipherTextBytes = memoryStream.ToArray();
                    cryptoStream.Close();
                }
                memoryStream.Close();
            }
            return Convert.ToBase64String(cipherTextBytes);
        }



        private void label1_Click(object sender, EventArgs e)
        {
            ///some test, so i hard-code the layout in c# and i decompile it afterwords
            //string xeno = EncryptionHelper.Encrypt("efozkoepfk");//t1F9IUlnEocOikJt+WZeQeu5CuU46CdDwdYtWJ9NSVM=
            //string code = EncryptionHelper.Decrypt("t1F9IUlnEocOikJt+WZeQeu5CuU46CdDwdYtWJ9NSVM=");
            ///

            ///decompiled code : call goes AFTER the ldstr
            /* 0x0000056D 728D000070   *///IL_0001: ldstr     "t1F9IUlnEocOikJt+WZeQeu5CuU46CdDwdYtWJ9NSVM="
            /* 0x00000572 2815000006   *///IL_0006: call      string simple_str_obfuscation_dnlib.EncryptionHelper::Decrypt(string)
            ///
        }
    }
}
