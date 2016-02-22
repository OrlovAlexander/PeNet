﻿/***********************************************************************
Copyright 2016 Stefan Hausotte

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

*************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace PeNet
{
    public class PeFile
    {
        /// <summary>
        /// Returns true if all section are parseable
        /// and the magic "MZ" header is set.
        /// </summary>
        public bool IsValidPeFile
        {
            get
            {
                return (HasValidExceptionDir
                    && HasValidExportDir
                    && HasValidImportDir
                    && HasValidResourceDir
                    && HasValidSecurityDir
                    && (ImageDosHeader.e_magic == 0x5a4d));
            }
        }

        /// <summary>
        /// Returns true if the Export directory is valid.
        /// </summary>
        public bool HasValidExportDir { get; private set; } = true;

        /// <summary>
        /// Returns true if the Import directory is valid.
        /// </summary>
        public bool HasValidImportDir { get; private set; } = true;

        /// <summary>
        /// Returns true if the Resource directory is valid.
        /// </summary>
        public bool HasValidResourceDir { get; private set; } = true;

        /// <summary>
        /// Returns true if the Exception directory in x64 applications
        /// is valid.
        /// </summary>
        public bool HasValidExceptionDir { get; private set; } = true;

        /// <summary>
        /// Returns true if the Security directory is valid.
        /// </summary>
        public bool HasValidSecurityDir { get; private set; } = true;

        /// <summary>
        /// True if the PE file is a DLL
        /// </summary>
        public bool IsDLL => ((ImageNtHeaders.FileHeader.Characteristics & 0x2000) == 0x2000);

        /// <summary>
        /// True if the PE file is a EXE
        /// </summary>
        public bool IsEXE => ((ImageNtHeaders.FileHeader.Characteristics & 0x02) == 0x02);

        /// <summary>
        /// Represents an exported function from the export table.
        /// </summary>
        public class ExportFunction
        {
            /// <summary>
            /// Exported function name.
            /// </summary>
            public string Name { get; private set; }

            /// <summary>
            /// Exported function address.
            /// </summary>
            public UInt32 Address { get; private set; }

            /// <summary>
            /// Exported function ordinal.
            /// </summary>
            public UInt16 Ordinal { get; private set; }

            public ExportFunction(string name, UInt32 address, UInt16 ordinal)
            {
                Name = name;
                Address = address;
                Ordinal = ordinal;
            }

            public override string ToString()
            {
                var sb = new StringBuilder("ExportFunction\n");
                sb.Append(Utility.PropertiesToString(this, "{0,-20}:\t{1,10:X}\n"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Represents an imported function from the import table.
        /// </summary>
        public class ImportFunction
        {
            /// <summary>
            /// Name of the imported function.
            /// </summary>
            public string Name { get; private set; }

            /// <summary>
            /// DLL top which the function belongs.
            /// </summary>
            public string DLL { get; private set; }

            /// <summary>
            /// Hint for the imported function.
            /// </summary>
            public UInt16 Hint { get; private set; }

            public ImportFunction(string name, string dll, UInt16 hint)
            {
                Name = name;
                DLL = dll;
                Hint = hint;
            }

            public override string ToString()
            {
                var sb = new StringBuilder("ImportFunction\n");
                sb.Append(Utility.PropertiesToString(this, "{0,-20}:\t{1,10:X}\n"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Information about the Certificate Revocation List,
        /// if any is present in a signed binary. 
        /// </summary>
        public class CrlUrlList
        {
            /// <summary>
            /// List with Certificate Revocation Locations.
            /// </summary>
            public List<string> Urls { get; set; }

            public CrlUrlList(byte[] rawData)
            {
                Urls = new List<string>();
                ParseCrls(rawData);
            }

            public CrlUrlList(X509Certificate2 cert)
            {
                Urls = new List<string>();
                foreach (var ext in cert.Extensions)
                {
                    if (ext.Oid.Value == "2.5.29.31")
                    {
                        ParseCrls(ext.RawData);
                    }
                }
            }

            private void ParseCrls(byte[] rawData)
            {
                var rawLength = rawData.Length;
                for (int i = 0; i < rawLength - 5; i++)
                {
                    // Find a HTTP(s) string.
                    if ((rawData[i] == 'h'
                        && rawData[i + 1] == 't'
                        && rawData[i + 2] == 't'
                        && rawData[i + 3] == 'p'
                        && rawData[i + 4] == ':')
                        || (rawData[i] == 'l'
                        && rawData[i+1] == 'd'
                        && rawData[i+2] == 'a'
                        && rawData[i+3] == 'p'
                        && rawData[i+4] == ':'))
                    {
                        var bytes = new List<byte>();
                        for(int j = i; j < rawLength; j++)
                        {
                            if ((rawData[j-4] == '.'
                                && rawData[j-3] == 'c'
                                && rawData[j-2] == 'r'
                                && rawData[j-1] == 'l') 
                                || (rawData[j] == 'b'
                                && rawData[j+1] == 'a'
                                && rawData[j+2] == 's'
                                && rawData[j+3] == 'e'
                                ))
                            {
                                i = j;
                                break;
                            }
                                

                            if (rawData[j] < 0x20 || rawData[j] > 0x7E)
                            {
                                i = j;
                                break;
                            }

                            bytes.Add(rawData[j]);
                            
                        }
                        var uri = Encoding.ASCII.GetString(bytes.ToArray());

                        if (IsValidUri(uri) && uri.StartsWith("http://") && uri.EndsWith(".crl"))
                            Urls.Add(uri);

                        if (uri.StartsWith("ldap:", StringComparison.InvariantCulture))
                        {
                            uri = "ldap://" + uri.Split('/')[2];
                            Urls.Add(uri);
                        }
                    }

                }
            }

            private bool IsValidUri(string uri)
            {
                Uri uriResult;
                return Uri.TryCreate(uri, UriKind.Absolute, out uriResult)
                    && (uriResult.Scheme == Uri.UriSchemeHttp
                    || uriResult.Scheme == Uri.UriSchemeHttps);
            }


            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine("CRL URLs:");
                foreach(var url in Urls)
                    sb.AppendFormat("\t{0}\n", url);
                return sb.ToString();
                
            }
        }

        public IMAGE_DOS_HEADER ImageDosHeader { get; private set; }
        public IMAGE_NT_HEADERS ImageNtHeaders { get; private set; }
        public IMAGE_SECTION_HEADER[] ImageSectionHeaders { get; private set; }
        public IMAGE_EXPORT_DIRECTORY ImageExportDirectory { get; private set; }
        public IMAGE_IMPORT_DESCRIPTOR[] ImageImportDescriptors { get; private set; }
        public ExportFunction[] ExportedFunctions { get; private set; }
        public ImportFunction[] ImportedFunctions { get; private set; }
        public IMAGE_RESOURCE_DIRECTORY[] ImageResourceDirectory { get; private set; }
        public RUNTIME_FUNCTION[] RuntimeFunctions { get; private set; }
        public WIN_CERTIFICATE WinCertificate { get; private set; }

        /// <summary>
        /// A X509 PKCS7 signature if the PE file was digitally signed with such
        /// a signature.
        /// </summary>
        public X509Certificate2 PKCS7 { get; private set; }

        /// <summary>
        /// True if the PE file is a x64 binary.
        /// </summary>
        public bool Is64Bit { get; private set; }

        /// <summary>
        /// True if the PE file is a x32 binary.
        /// </summary>
        public bool Is32Bit { get { return !Is64Bit; } }

        private byte[] _buff;

        public PeFile(byte [] buff)
        {
            UInt32 secHeaderOffset = 0;
            _buff = buff;

            ImageDosHeader = new IMAGE_DOS_HEADER(buff);
            // Check if the PE file is 64 bit.
            Is64Bit = (Utility.BytesToUInt16(buff, ImageDosHeader.e_lfanew + 0x4) == Constants.IMAGE_FILE_MACHINE_AMD64);

            secHeaderOffset = (UInt32)(Is64Bit ? 0x108 : 0xF8);

            ImageNtHeaders = new IMAGE_NT_HEADERS(buff, ImageDosHeader.e_lfanew, Is64Bit);

            ImageSectionHeaders = ParseImageSectionHeaders(
                buff,
                ImageNtHeaders.FileHeader.NumberOfSections,
                ImageDosHeader.e_lfanew + secHeaderOffset
                );

            if (ImageNtHeaders.OptionalHeader.DataDirectory[(int) Constants.DataDirectoryIndex.Export].VirtualAddress != 0)
            {
                try
                {
                    ImageExportDirectory = new IMAGE_EXPORT_DIRECTORY(
                        buff,
                        Utility.RVAtoFileMapping(ImageNtHeaders.OptionalHeader.DataDirectory[0].VirtualAddress,
                        ImageSectionHeaders)
                        );

                    ExportedFunctions = ParseExportedFunctions(
                        buff,
                        ImageExportDirectory,
                        ImageSectionHeaders
                        );
                }
                catch
                {
                    // No or invalid export directory.
                    HasValidExportDir = false;
                }
            }

            if (ImageNtHeaders.OptionalHeader.DataDirectory[1].VirtualAddress != 0)
            {
                try
                {
                    ImageImportDescriptors = ParseImportDescriptors(
                    buff,
                    Utility.RVAtoFileMapping(ImageNtHeaders.OptionalHeader.DataDirectory[(int) Constants.DataDirectoryIndex.Import].VirtualAddress, ImageSectionHeaders),
                    ImageSectionHeaders
                    );

                    ImportedFunctions = ParseImportedFunctions(buff, ImageImportDescriptors, ImageSectionHeaders);
                }
                catch
                {
                    // No or invalid import directory.
                    HasValidImportDir = false;
                }
            }

            // Parse the resource directory.
            if(ImageNtHeaders.OptionalHeader.DataDirectory[2].VirtualAddress != 0)
            {
                try
                {
                    ImageResourceDirectory = ParseImageResourceDirectory(
                        buff,
                        Utility.RVAtoFileMapping(ImageNtHeaders.OptionalHeader.DataDirectory[(int) Constants.DataDirectoryIndex.Resource].VirtualAddress, ImageSectionHeaders),
                        ImageSectionHeaders
                        );
                }
                catch
                {
                    // No or invalid resource directory.
                    ImageResourceDirectory = null;
                    HasValidResourceDir = false;
                }
            }

            // Parse x64 Exception directory
            if(Is64Bit)
            {
                if(ImageNtHeaders.OptionalHeader.DataDirectory[(UInt32) Constants.DataDirectoryIndex.Exception].VirtualAddress != 0)
                {
                    try
                    {
                        RuntimeFunctions = PareseExceptionDirectory(
                        buff,
                        Utility.RVAtoFileMapping(ImageNtHeaders.OptionalHeader.DataDirectory[(UInt32) Constants.DataDirectoryIndex.Exception].VirtualAddress, ImageSectionHeaders),
                        ImageNtHeaders.OptionalHeader.DataDirectory[(UInt32) Constants.DataDirectoryIndex.Exception].Size,
                        ImageSectionHeaders
                        );
                    }
                    catch
                    {
                        // No or invalid Exception directory.
                        RuntimeFunctions = null;
                        HasValidExceptionDir = false;
                    }
                }
            }

            // Parse the security directory for certificates
            if(ImageNtHeaders.OptionalHeader.DataDirectory[(int) Constants.DataDirectoryIndex.Security].VirtualAddress != 0)
            {
                try
                {
                    WinCertificate = ParseImageSecurityDirectory(
                    buff,
                   ImageNtHeaders.OptionalHeader.DataDirectory[(int) Constants.DataDirectoryIndex.Security].VirtualAddress,
                    ImageSectionHeaders);
                }
                catch(Exception)
                {
                    // Invalid Security Directory
                    WinCertificate = null;
                    HasValidSecurityDir = false;
                }
            }
        }

        public PeFile(string peFile)
            : this(File.ReadAllBytes(peFile)) { }

        /// <summary>
        /// Get an object which holds information about
        /// the Certificate Revocation Lists of the signing
        /// certificate if any is present.
        /// </summary>
        /// <returns>Certificate Revocation List information or null if binary is not signed.</returns>
        public CrlUrlList GetCrlUrlList()
        {
            if (PKCS7 == null)
                return null;
            else
                return new CrlUrlList(PKCS7);
        }

        public UNWIND_INFO GetUnwindInfo(RUNTIME_FUNCTION runtimeFunction)
        {
            UInt32 uwAddress = 0x00;

            // Check if the last bit is set in the UnwindInfo. If so, it is a chained 
            // information.
            if((runtimeFunction.UnwindInfo & 0x1) == 0x1)
            {
                uwAddress = runtimeFunction.UnwindInfo & 0xFFFE;
            }
            else
            {
                uwAddress = runtimeFunction.UnwindInfo;
            }

            var uw = new UNWIND_INFO(_buff, Utility.RVAtoFileMapping(uwAddress, ImageSectionHeaders));
            return uw;
        }

        public WIN_CERTIFICATE ParseImageSecurityDirectory(byte[] buff, UInt32 dirOffset, IMAGE_SECTION_HEADER[] sh)
        {
            var wc = new WIN_CERTIFICATE(buff, dirOffset);

            if(wc.wCertificateType == Constants.WIN_CERT_TYPE_PKCS_SIGNED_DATA)
            {
                var cert = wc.bCertificate;
                PKCS7 = new X509Certificate2(cert);
            }

            return wc;
        }

        ImportFunction[] ParseImportedFunctions(byte[] buff, IMAGE_IMPORT_DESCRIPTOR[] idescs, IMAGE_SECTION_HEADER[] sh)
        {
            var impFuncs = new List<ImportFunction>();
            UInt32 sizeOfThunk = (UInt32)(Is64Bit ? 0x8 : 0x4); // Size of IMAGE_THUNK_DATA
            UInt64 ordinalBit = (UInt64)(Is64Bit ? 0x8000000000000000 : 0x80000000);
            UInt64 ordinalMask = (UInt64)(Is64Bit ? 0x7FFFFFFFFFFFFFFF : 0x7FFFFFFF);

            foreach (var idesc in idescs)
            {
                var dllAdr = Utility.RVAtoFileMapping(idesc.Name, sh);
                var dll = Utility.GetName(dllAdr, buff);
                var tmpAdr = (idesc.OriginalFirstThunk != 0) ? idesc.OriginalFirstThunk : idesc.FirstThunk;
                if (tmpAdr == 0)
                    continue;

                var thunkAdr = Utility.RVAtoFileMapping(tmpAdr, sh);
                UInt32 round = 0;
                while (true)
                {
                    var t = new IMAGE_THUNK_DATA(buff, thunkAdr + round * sizeOfThunk, Is64Bit);

                    if (t.AddressOfData == 0)
                        break;

                    // Check if import by name or by ordinal.
                    // If it is an import by ordinal, the most significant bit of "Ordinal" is "1" and the ordinal can
                    // be extracted from the least significant bits.
                    // Else it is an import by name and the link to the IMAGE_IMPORT_BY_NAME has to be followed

                    if ((t.Ordinal & ordinalBit) == ordinalBit) // Import by ordinal
                    {
                        impFuncs.Add(new ImportFunction(null, dll, (UInt16)(t.Ordinal & ordinalMask)));
                    }
                    else // Import by name
                    {
                        var ibn = new IMAGE_IMPORT_BY_NAME(buff, Utility.RVAtoFileMapping(t.AddressOfData, sh));
                        impFuncs.Add(new ImportFunction(ibn.Name, dll, ibn.Hint));
                    }

                    round++;
                }
            }

            return impFuncs.ToArray();
        }


        IMAGE_IMPORT_DESCRIPTOR[] ParseImportDescriptors(byte[] buff, UInt32 offset, IMAGE_SECTION_HEADER[] sh)
        {
            var idescs = new List<IMAGE_IMPORT_DESCRIPTOR>();
            UInt32 idescSize = 20; // Size of IMAGE_IMPORT_DESCRIPTOR (5 * 4 Byte)
            UInt32 round = 0;

            while (true)
            {
                var idesc = new IMAGE_IMPORT_DESCRIPTOR(buff, offset + idescSize * round);

                // Found the last IMAGE_IMPORT_DESCRIPTOR which is completely null (except TimeDateStamp).
                if (idesc.OriginalFirstThunk == 0
                    //&& idesc.TimeDateStamp == 0
                    && idesc.ForwarderChain == 0
                    && idesc.Name == 0
                    && idesc.FirstThunk == 0)
                {
                    break;
                }

                idescs.Add(idesc);
                round++;
            }

            return idescs.ToArray();
        }

        ExportFunction[] ParseExportedFunctions(byte[] buff, IMAGE_EXPORT_DIRECTORY ed, IMAGE_SECTION_HEADER[] sh)
        {
            var expFuncs = new ExportFunction[ed.NumberOfNames];
            var funcOffsetPointer = Utility.RVAtoFileMapping(ed.AddressOfFunctions, sh);
            var ordOffset = Utility.RVAtoFileMapping(ed.AddressOfNameOrdinals, sh);
            var nameOffsetPointer = Utility.RVAtoFileMapping(ed.AddressOfNames, sh);

            var funcOffset = Utility.BytesToUInt32(buff, funcOffsetPointer);

            for (UInt32 i = 0; i < expFuncs.Length; i++)
            {
                var namePtr = Utility.BytesToUInt32(buff, nameOffsetPointer + sizeof(UInt32) * i);
                var nameAdr = Utility.RVAtoFileMapping(namePtr, sh);
                var name = Utility.GetName(nameAdr, buff);
                var ordinalIndex = (UInt32)Utility.GetOrdinal(ordOffset + sizeof(UInt16) * i, buff);
                var ordinal = ordinalIndex + ed.Base;
                var address = Utility.BytesToUInt32(buff, funcOffsetPointer + sizeof(UInt32) * ordinalIndex);

                expFuncs[i] = new ExportFunction(name, address, (UInt16)ordinal);
            }

            return expFuncs;
        }

        IMAGE_SECTION_HEADER[] ParseImageSectionHeaders(byte[] buff, UInt16 numOfSections, UInt32 offset)
        {
            var sh = new IMAGE_SECTION_HEADER[numOfSections];
            UInt32 secSize = 0x28; // Every section header is 40 bytes in size.
            for (UInt32 i = 0; i < numOfSections; i++)
            {
                sh[i] = new IMAGE_SECTION_HEADER(buff, offset + i * secSize);
            }

            return sh;
        }

        /// <summary>
        /// http://www.brokenthorn.com/Resources/OSDevPE.html
        /// </summary>
        /// <param name="buff">Byte buffer with the whole binary.</param>
        /// <param name="offsetFirstRescDir">Offset to the first resource directory (= DataDirectory[2].VirtualAddress)</param>
        /// <param name="sh">Image section headers of the binary.</param>
        /// <returns>List with resource directories.</returns>
        private IMAGE_RESOURCE_DIRECTORY[] ParseImageResourceDirectory(byte[] buff, UInt32 offsetFirstRescDir, IMAGE_SECTION_HEADER[] sh)
        {
            var sizeOfEntry = 0x8;
            var sizeOfRescDir = 0x10;
            var rescDirs = new List<IMAGE_RESOURCE_DIRECTORY>();
            var firstDir = new IMAGE_RESOURCE_DIRECTORY(buff, offsetFirstRescDir);

            var numOfDirs = firstDir.NumberOfIdEntries + firstDir.NumberOfNameEntries;

            // Loop through the entire directory
            for (int i = 0; i < numOfDirs; i++)
            {
                var entry = new IMAGE_RESOURCE_DIRECTORY_ENTRY(buff, (UInt32)(offsetFirstRescDir + sizeOfRescDir + i * sizeOfEntry));
                if (entry.DataIsDirectory)
                {
                    // It can happen that the IMAGE_RESOURCE_DIRECTORY is not valid, but Windows will parse it anyways...
                    try
                    {
                        var tmpResc = new IMAGE_RESOURCE_DIRECTORY(buff, offsetFirstRescDir + entry.OffsetToDirectory);
                        rescDirs.Add(tmpResc);
                    }
                    catch(IndexOutOfRangeException)
                    {
                        rescDirs.Add(null);
                    }
                }
            }

            return rescDirs.ToArray();
        }

        private RUNTIME_FUNCTION[] PareseExceptionDirectory(byte[] buff, UInt32 offset, UInt32 size, IMAGE_SECTION_HEADER[] sh)
        {
            var sizeOfRuntimeFunction = 0xC;
            var rf = new RUNTIME_FUNCTION[size / sizeOfRuntimeFunction];

            for (int i = 0; i < rf.Count(); i++)
            {
                rf[i] = new RUNTIME_FUNCTION(buff, (UInt32)(offset + i * sizeOfRuntimeFunction));
            }

            return rf;
        }

        /// <summary>
        /// Tries to parse the PE file. If no exceptions are thrown, true
        /// </summary>
        /// <param name="file">Path to a possible PE file.</param>
        /// <returns>True if the file could be parsed as a PE file, else false.</returns>
        public static bool IsValidPEFile(string file)
        {
            PeNet.PeFile pe = null;
            try
            {
                pe = new PeNet.PeFile(file);
            }
            catch
            {
                return false;
            }
            return pe.IsValidPeFile;
        }

        /// <summary>
        /// Returns if the PE file is a EXE, DLL and which architecture
        /// is used (32/64).
        /// Architectures: "I386", "AMD64", "UNKNOWN"
        /// DllOrExe: "DLL", "EXE", "UNKNOWN"
        /// </summary>
        /// <returns>
        /// A string "architecture_dllOrExe".
        /// E.g. "AMD64_DLL"
        /// </returns>
        public string GetFileType()
        {
            string fileType;

            switch(ImageNtHeaders.FileHeader.Machine)
            {
                case Constants.IMAGE_FILE_MACHINE_I386:
                    fileType = "I386";
                    break;
                case Constants.IMAGE_FILE_MACHINE_AMD64:
                    fileType = "AMD64";
                    break;
                default:
                    fileType = "UNKNOWN";
                    break;
            }

            if ((ImageNtHeaders.FileHeader.Characteristics & Constants.IMAGE_FILE_DLL) != 0)
                fileType += "_DLL";
            else if ((ImageNtHeaders.FileHeader.Characteristics & Constants.IMAGE_FILE_EXECUTABLE_IMAGE) != 0)
                fileType += "_EXE";
            else
                fileType += "_UNKNOWN";


            return fileType;
        }

        /// <summary>
        /// Mandiant’s imphash convention requires the following:
        /// Resolving ordinals to function names when they appear.
        /// Converting both DLL names and function names to all lowercase.
        /// Removing the file extensions from imported module names.
        /// Building and storing the lowercased strings in an ordered list.
        /// Generating the MD5 hash of the ordered list.
        /// 
        /// oleaut32, ws2_32 and wsock32 can resolve ordinals to functions names.
        /// The implementation is equal to the python module "pefile" 1.2.10-139
        /// https://code.google.com/p/pefile/
        /// </summary>
        /// <returns>The ImpHash of the PE file.</returns>
        public string GetImpHash()
        {
            if (ImportedFunctions == null || ImportedFunctions.Length == 0)
                return null;

            var list = new List<string>();
            foreach(var impFunc in ImportedFunctions)
            {
                var tmp = impFunc.DLL.Split('.')[0];
                tmp += ".";
                if(impFunc.Name == null) // Import by ordinal
                {
                    if (impFunc.DLL == "oleaut32.dll")
                    {
                        tmp += OrdinalSymbolMapping.Lookup(OrdinalSymbolMapping.Modul.oleaut32, impFunc.Hint);
                    }
                    else if (impFunc.DLL == "ws2_32.dll")
                    {
                        tmp += OrdinalSymbolMapping.Lookup(OrdinalSymbolMapping.Modul.ws2_32, impFunc.Hint);
                    }
                    else if (impFunc.DLL == "wsock32.dll")
                    {
                        tmp += OrdinalSymbolMapping.Lookup(OrdinalSymbolMapping.Modul.wsock32, impFunc.Hint);
                    }
                    else // cannot resolve ordinal to a function name
                    {
                        tmp += "ord";
                        tmp += impFunc.Hint.ToString();
                    }
                }
                else // Import by name
                {
                    tmp += impFunc.Name;
                }

                list.Add(tmp.ToLower());
            }

            // Concatenate all imports to one string separated by ','.
            var imports = string.Join(",", list);

            var md5 = System.Security.Cryptography.MD5.Create();
            var inputBytes = System.Text.Encoding.ASCII.GetBytes(imports);
            var hash = md5.ComputeHash(inputBytes);
            var sb = new StringBuilder();
            for(int i = 0; i<hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
