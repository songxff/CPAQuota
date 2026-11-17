using System;
using System.IO;
using System.Windows.Forms;
namespace CpaTaskbar {
 static class TestRunner {
  [STAThread]static int Main(string[] args){
   try{Native.SetProcessDpiAwarenessContext(new IntPtr(-4));}catch{}
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   if(args.Length!=2)throw new ArgumentException("Usage: CPAQuota.Tests.exe --report file.json | --render-preview folder | --smoke-taskbar file.json");
   try{
    if(args[0]=="--render-preview"){SelfTests.Render(args[1]);return 0;}
    if(args[0]=="--smoke-taskbar"){SelfTests.SmokeTaskbar(args[1]);return 0;}
    if(args[0]!="--report")throw new ArgumentException("Unknown test command.");
    SelfTests.Run(args[1]);Console.WriteLine("All tests passed.");return 0;
   }catch(Exception e){File.WriteAllText(args[1],J.Json(new{passed=false,error=e.ToString()}));Console.Error.WriteLine(e.Message);return 1;}
  }
 }
}
