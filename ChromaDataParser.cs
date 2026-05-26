using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

class Program
{
    static void Main(string[] args)
    {
        bool RenameLogFile = false;

        if (args.Length < 1)
        {
            Console.WriteLine("Usage: MyApp.exe <ChromaDataString>");
            Thread.Sleep(5000);
            return;
        }

        // 🔧 รวม args[] กลับเป็น string เดียว (เผื่อ args ถูกแบ่งจากช่องว่าง)
        string rawInput = string.Join(" ", args).Trim('"');

        //Console.WriteLine($"RAW INPUT = [{rawInput}] (Length={rawInput.Length})");
        string[] parts = rawInput
            .Split('}')                 // แยกด้วย '}'
            .Select(p => p.Trim())      // ตัดช่องว่าง
            .ToArray();

        // แสดงข้อมูลดิบจาก Chroma
        //Console.WriteLine("RAW INPUT = " + rawInput);
        //Console.WriteLine("=== Raw Chroma Data ===");
        //for (int i = 0; i < parts.Length; i++)
        //    Console.WriteLine($"Part[{i}] = {parts[i]}");
        //    Thread.Sleep(15000);
        // แสดงข้อมูลดิบจาก Chroma

        if (parts.Length != 12)
        {
            Console.WriteLine("Invalid input format from Chroma.");
            Console.WriteLine("Raw input: " + args[0]);
            Console.WriteLine("Parts Length: " + parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                Console.WriteLine($"[{i}] {parts[i]}");
            }
            Thread.Sleep(5000);
            return;
        }

        string serialNo = parts[0].Trim();
        string workOrder = parts[1];
        string modelName = parts[2];
        string lineName = parts[3];
        string sectionName = parts[4];
        string groupName = parts[5];
        string stationName = parts[6];
        string testerId = parts[7];
        string fixtureId = parts[8];
        string carrierId = parts[9];
        string carrierSide = parts[10];
        string testResult = parts[11].Trim().ToUpper();
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string lastFileName = "";

        // ====== โหลด Config.ini ======
        string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string iniPath = Path.Combine(exeDirectory, "Config.ini");
        IniReader ini = new IniReader(iniPath);

        string logDirectory = @"D:\Chroma Temp Data";
        string outputDirectory = @"\\10.150.208.54\shopfloor$\testdata\monitor";

        var failedTests = new List<FailedTestSequence>();

        //Thread.Sleep(30000); // รอ log ถูกเขียน
        for (int i = 0; i < 500; i++)
        {
            //string status,SN = ChromaStatusChecker.WaitChromaRunningStatus();
            var (status, sn) = ChromaStatusChecker.WaitChromaRunningStatus();
            serialNo = sn;
            Console.WriteLine($"Loop {i + 1}: Status = {status}");

            if (status == "NO")//if (status == "YES" || status == "NO")
            {
                // ถ้าเจอสถานะที่ต้องการ ออกลูปได้เลย
                break;
            }

            // หน่วงเวลา 500 ms
            Thread.Sleep(500);
            if (i > 10)
            {
                RenameLogFile = true ;
            }
        }
        //Thread.Sleep(30000); // รอ log ถูกเขียน
        Thread.Sleep(500);
        string mesStatus = ChromaStatusChecker.CheckMesUpdateStatus();       
        if (mesStatus == "YES")
        {
            //Console.WriteLine("MES กำลังอัปเดต...");
            MesUpdate();
        }
        else if (mesStatus == "NO")
        { 
            CheckForUpdate(); 
        }
        else
        {
            Console.WriteLine("mesStatus: " + mesStatus);
            Thread.Sleep(15000);
        }
        // ====== FAIL → หา log + รายละเอียด ======
        if (testResult == "FAIL")
        {
            if (!Directory.Exists(logDirectory))
            {
                Console.WriteLine("❌ Folder not found: " + logDirectory);
                Thread.Sleep(5000);
                return;
            }

            string[] files = Directory.EnumerateFiles(logDirectory, "*.txt")
                .Where(f => Path.GetFileName(f).StartsWith(serialNo))
                .ToArray();

            if (files.Length == 0)
            {
                Console.WriteLine("📂 All files in the folder:");
                foreach (var file in Directory.GetFiles(logDirectory, "*.txt"))
                    Console.WriteLine(" - " + Path.GetFileName(file));

                Console.WriteLine("SerialNo=[" + serialNo + "]");
                Console.WriteLine("❌ No file found matching serialNo");
                Thread.Sleep(5000);
            }
            else
            {
                foreach (string file in files)
                    Console.WriteLine("✅ File found: " + Path.GetFileName(file));
            }

            if (files.Length > 0)
            {
                string latestFile = files.OrderByDescending(f => File.GetLastWriteTime(f)).First();
                lastFileName = latestFile;
                string fileContent = File.ReadAllText(latestFile);

                var matches = Regex.Matches(
                    fileContent,
                    @"\(UUT Test seq\.(\d+)\)\s*:\s*(.*?)\s*-+\s*FAIL",
                    RegexOptions.Multiline);

                foreach (Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out int seqNum))
                    {
                        var test = new FailedTestSequence
                        {
                            SequenceNumber = seqNum,
                            Title = match.Groups[2].Value.Trim()
                        };

                        string seqPattern = $@"\(UUT Test seq\.{seqNum}\)(.*?)(?=\(UUT Test seq\.|\Z)";
                        var seqMatch = Regex.Match(fileContent, seqPattern, RegexOptions.Singleline);
                        if (seqMatch.Success)
                            test.Details = seqMatch.Value.Trim().Replace(",", ";");

                        failedTests.Add(test);
                    }
                }
            }
        }

        // ====== PASS → dummy ======
        if (testResult == "PASS")
        {
            failedTests.Add(new FailedTestSequence
            {
                SequenceNumber = 0,
                Title = "N/A",
                Details = "No failure, test passed"
            });
            lastFileName = "N/A";
        }

        // ====== Path output ======
        string yearFolder = DateTime.Now.ToString("yyyy");
        string monthFolder = DateTime.Now.ToString("MM");
        string modelFolder = modelName;
        string outputPath = Path.Combine(outputDirectory, yearFolder, monthFolder, modelFolder);
        Directory.CreateDirectory(outputPath);

        string outPath = Path.Combine(outputPath, "testreport.csv");
        bool writeHeader = !File.Exists(outPath);

        // ====== เขียน CSV ======
        using (StreamWriter writer = new StreamWriter(outPath, append: true))
        {
            if (writeHeader)
            {
                writer.WriteLine("SerialNo,WorkOrder,ModelName,LineName,SectionName,GroupName,StationName,TesterId,FixtureId,CarrierId,CarrierSide,TimeStamp,TestResult,SequenceNumber,Title,FileName");
            }

            foreach (var fail in failedTests)
            {
                writer.WriteLine(string.Join(",", new string[]
                {
                    EscapeCsv(serialNo),
                    EscapeCsv(workOrder),
                    EscapeCsv(modelName),
                    EscapeCsv(lineName),
                    EscapeCsv(sectionName),
                    EscapeCsv(groupName),
                    EscapeCsv(stationName),
                    EscapeCsv(testerId),
                    EscapeCsv(fixtureId),
                    EscapeCsv(carrierId),
                    EscapeCsv(carrierSide),
                    EscapeCsv(timestamp),
                    EscapeCsv(testResult),
                    fail.SequenceNumber.ToString(),
                    EscapeCsv(fail.Title),
                    EscapeCsv(lastFileName)
                }));
            }
        }

        Console.WriteLine("Add data to: " + outPath);

        //Rename Log file
        if(RenameLogFile)
        {

            DataCollectorClose(); //Old DataCollector
            ATSDataCollectorUpdate(); //New ATSDataCollectorUpdate .bat Close,Update,Open
            string baseFileName = ChromaStatusChecker.ReadTPName();
            string directory = Path.Combine(@"C:\Program Files (x86)\Chroma\SMPS ATS\Log", baseFileName);
            Console.WriteLine("Waiting MES Update.." );
            Thread.Sleep(15000);
            RenameMDB(directory, baseFileName);
            //run UserUpdate.ps1
            DataCollectorOpen();
            Thread.Sleep(5000); // Wait update
            string serverPath = @"\\10.150.208.54\ats_common\Chroma Released\ChromaDataParser\UserUpdate.ps1";
            string localPath = @"D:\ChromaDataParser\UserUpdate\UserUpdate.ps1";
            PSRunScript(serverPath, localPath);

        }

        // 🔧 เช็ค Auto Update ChromaDataParser.exe
        CheckForUpdate();
    }

    static string EscapeCsv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
        {
            s = s.Replace("\"", "\"\"");
            return $"\"{s}\"";
        }
        return s;
    }

    static void PSRunScript(string serverPath, string localPath)
    {
        // ✅ ตรวจสอบโฟลเดอร์ local ว่ามีไหม ถ้าไม่มีให้สร้าง
        string localDir = Path.GetDirectoryName(localPath);
        if (!Directory.Exists(localDir))
        {
            Directory.CreateDirectory(localDir);
        }

        // ✅ Copy จาก server มา local (overwrite ถ้ามีแล้ว)
        try
        {
            File.Copy(serverPath, localPath, true);
            Console.WriteLine($"✅ Copy file from server: {serverPath} → {localPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to copy file: {ex.Message}");
            return;
        }

        // ✅ เช็คว่าไฟล์ .ps1 มีอยู่จริง
        if (!File.Exists(localPath))
        {
            Console.WriteLine($"❌ ไม่พบไฟล์ PowerShell Script: {localPath}");
            return;
        }

        // 🔗 Run PowerShell
        var psi = new ProcessStartInfo
        {
            FileName = @"C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe", // ใช้ 32-bit PowerShell
            Arguments = $"-ExecutionPolicy Bypass -File \"{localPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = new Process())
        {
            process.StartInfo = psi;
            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            Console.WriteLine("---- OUTPUT ----");
            Console.WriteLine(output);

            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine("---- ERROR ----");
                Console.WriteLine(error);
            }
        }
    }
    static void RenameMDB(string directory, string baseFileName)
    {
        //string directory = @"D:\Test";
        //string baseFileName = "ECD16010101-07-FAT-C_06";

        //RenameMDB(directory, baseFileName);

        // วันที่ปัจจุบันในรูปแบบ YYYYMMDD
        string dateSuffix = DateTime.Now.ToString("yyyyMMdd");

        // สร้าง path ของไฟล์เต็ม (สมมติ .txt)
        string filePath = Path.Combine(directory, $"{baseFileName}@{dateSuffix}.MDB");

        if (!File.Exists(filePath))
        {
            Console.WriteLine("❌ File not found: " + filePath);
            return;
        }

        string filenameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);

        // เวลา ณ ปัจจุบัน HHmm
        string timeSuffix = DateTime.Now.ToString("HHmm");

        // สร้างชื่อใหม่ โดยต่อ _HHMM หลังชื่อไฟล์เดิม
        string newFileName = $"{filenameWithoutExt}_{timeSuffix}{extension}";
        string newFilePath = Path.Combine(directory, newFileName);

        File.Move(filePath, newFilePath);

        Console.WriteLine($"✅ File renamed to: {newFileName}");
    }
    static void DataCollectorClose()
    {
        // ใส่ชื่อ process ที่ต้องการตรวจสอบและปิด (ไม่ต้องใส่ .exe)
        string[] processesToCheck = { "DataCollector" };

        foreach (string pname in processesToCheck)
        {
            var processes = Process.GetProcessesByName(pname);
            if (processes.Length > 0)
            {
                foreach (var p in processes)
                {
                    try
                    {
                        Console.WriteLine($"Found {p.ProcessName} (PID={p.Id}) → closing...");
                        p.Kill();
                        p.WaitForExit();
                        Console.WriteLine($"{p.ProcessName} closed.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error closing {p.ProcessName}: {ex.Message}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"{pname} is not running.");
            }
        }

        Console.WriteLine("✅ Check complete.");
    }

    static void DataCollectorOpen()
    {
        string exePath = @"C:\Program Files\DataCollector\DataCollector.exe";

        try
        {
            if (!File.Exists(exePath))
            {
                Console.WriteLine($"❌ Executable not found at path: {exePath}");
                return;
            }
            Process.Start(exePath);
            Console.WriteLine("✅ Process started");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to start process: {ex.Message}");
        }
    }

    // ====== ฟังก์ชัน Auto Update ======
    static void CheckForUpdate()
    {
        // Path .bat อยู่ที่เดียวกับ exe
        string batPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.bat");

        // ตรวจสอบว่ามีไฟล์ .bat
        if (!File.Exists(batPath))
            return;

        try
        {
            // เรียก .bat ผ่าน cmd.exe
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true // true ถ้าอยากซ่อน console
            };

            Process.Start(psi);

            // ปิดโปรแกรมหลัก ให้ .bat ทำงาน
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ Failed to run updater .bat: " + ex.Message);
        }
    }

    // ====== ฟังก์ชัน MES Update ======
    static void MesUpdate()
    {
        string batPath = @"D:\ATSDataUploadApp\executemes.bat";

        if (!File.Exists(batPath))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                UseShellExecute = false,
                CreateNoWindow = false // true ถ้าอยากซ่อน console
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ Failed to run MES update .bat: " + ex.Message);
        }
    }

    // ====== ฟังก์ชัน MES Update ======
    static void ATSDataCollectorUpdate()
    {
        string batPath = @"D:\Delta Software\ATSDataCollector\update.bat";

        if (!File.Exists(batPath))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                UseShellExecute = false,
                CreateNoWindow = false // true ถ้าอยากซ่อน console
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ Failed to run ATSDataCollector update .bat: " + ex.Message);
        }
    }

}

// ===== คลาสอ่านไฟล์ INI =====
class IniReader
{
    private Dictionary<string, string> data = new Dictionary<string, string>();

    public IniReader(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found INI: " + filePath);

        foreach (var line in File.ReadAllLines(filePath))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("[")) continue;

            var parts = trimmed.Split('=');
            if (parts.Length >= 2)
                data[parts[0].Trim()] = parts[1].Trim();
        }
    }

    public string GetValue(string key, string defaultValue = "")
    {
        return data.ContainsKey(key) ? data[key] : defaultValue;
    }
}
static class IniHelper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern long WritePrivateProfileString(string section, string key, string value, string filePath);

    public static void WriteValue(string iniPath, string section, string key, string value)
    {
        WritePrivateProfileString(section, key, value, iniPath);
    }
}
// ===== คลาสเก็บข้อมูล FAIL =====
class FailedTestSequence
{
    public int SequenceNumber { get; set; }
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
}

// ===== คลาสเช็คสถานะ Chroma =====
static class ChromaStatusChecker
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern int GetPrivateProfileString(
        string section, string key, string defaultValue,
        StringBuilder returnValue, int size, string filePath);

    private static string INI_Read(string section, string key, string path)
    {
        var buffer = new StringBuilder(255);
        GetPrivateProfileString(section, key, "", buffer, buffer.Capacity, path);
        return buffer.ToString();
    }
    public static string ReadTPName()
    {
        string iniPath = @"C:\Program Files (x86)\Chroma\SMPS ATS\ShopFloor\ATS_Status.ini";

        if (!File.Exists(iniPath))
            iniPath = @"C:\Program Files\Chroma\SMPS ATS\ShopFloor\ATS_Status.ini";

        if (!File.Exists(iniPath))
            return ""; // คืนค่าว่างถ้าไฟล์ไม่เจอ

        // ใช้ INI_Read เหมือนใน ChromaStatusChecker
        string tpName = INI_Read("TP", "NAME", iniPath);

        return tpName;
    }

    public static (string Status, string SN) WaitChromaRunningStatus()
    {
        string sChromaIniPath = @"C:\Program Files (x86)\Chroma\SMPS ATS\ShopFloor\ATS_Status.ini";
        if (!File.Exists(sChromaIniPath))
            sChromaIniPath = @"C:\Program Files\Chroma\SMPS ATS\ShopFloor\ATS_Status.ini";

        if (!File.Exists(sChromaIniPath))
            return ("ERROR", ""); // คืน Tuple แทน string เดียว

        string sTesterStat = INI_Read("STATUS", "RUNNING", sChromaIniPath);
        string sUUT_SN = INI_Read("UUT", "SN", sChromaIniPath);

        if (sTesterStat != "YES" && sTesterStat != "NO")
            sTesterStat = "NO";

        return (sTesterStat, sUUT_SN);
    }
    public static string CheckMesUpdateStatus()
    {
        string sChromaIniPath = @"D:\ChromaDataParser\Config.ini";
        if (!File.Exists(sChromaIniPath))
            sChromaIniPath = @"D:\ChromaDataParser\Config.ini";

        if (!File.Exists(sChromaIniPath))
            return "ERROR";

        string sTesterStat = INI_Read("MES", "MesUpdate", sChromaIniPath);

        if (sTesterStat != "YES" && sTesterStat != "NO")
            sTesterStat = "NO";

        return sTesterStat;
    }

}
