﻿using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace shader_playground {
  public partial class Editor : Form {
    string compilerPath_ = @"..\..\..\..\..\build\bin\Windows\Debug\xenia-gpu-shader-compiler.exe";

    FileSystemWatcher compilerWatcher_;

    public Editor() {
      InitializeComponent();

      var compilerBinPath = Path.Combine(Directory.GetCurrentDirectory(),
                                         Path.GetDirectoryName(compilerPath_));
      compilerWatcher_ = new FileSystemWatcher(compilerBinPath, "*.exe");
      compilerWatcher_.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
      compilerWatcher_.Changed += (object sender, FileSystemEventArgs e) => {
        if (e.Name == Path.GetFileName(compilerPath_)) {
          Invoke((MethodInvoker)delegate { Process(sourceCodeTextBox.Text); });
        }
      };
      compilerWatcher_.EnableRaisingEvents = true;

      wordsTextBox.Click += (object sender, EventArgs e) => {
        wordsTextBox.SelectAll();
        wordsTextBox.Copy();
      };

      this.sourceCodeTextBox.Click += (object sender, EventArgs e) => {
        Process(sourceCodeTextBox.Text);
      };
      sourceCodeTextBox.TextChanged += (object sender, EventArgs e) => {
        Process(sourceCodeTextBox.Text);
      };

      sourceCodeTextBox.Text = string.Join(
        "\r\n", new string[] {
"xps_3_0",
"dcl_texcoord1 r0",
"dcl_color r1.xy",
"exec",
"alloc colors",
"exec",
"tfetch1D r2, r0.y, tf0, FetchValidOnly=false",
"tfetch1D r2, r0.x, tf2",
"tfetch2D r3, r3.wx, tf13",
"tfetch2D r[aL+3], r[aL+5].wx, tf13, FetchValidOnly=false, UnnormalizedTextureCoords=true, MagFilter=linear, MinFilter=linear, MipFilter=point, AnisoFilter=max1to1, UseRegisterGradients=true, UseComputedLOD=false, UseRegisterLOD=true, OffsetX=-1.5, OffsetY=1.0",
"tfetch3D r31.w_01, r0.xyw, tf15",
"tfetchCube r5, r1.xyw, tf31",
"        setTexLOD r1.z",
"        setGradientH r1.zyx",
"(!p0)        setGradientV r1.zyx",
"        getGradients r5, r1.xy, tf3",
"        mad oC0, r0, r1.yyyy, c0",
"        mul r4.xyz, r1.xyzz, c5.xyzz",
"        mul r4.xyz, r1.xyzz, c[0 + aL].xyzz",
"        mul r4.xyz, r1.xyzz, c[6 + aL].xyzz",
"        mul r4.xyz, r1.xyzz, c[0 + a0].xyzz",
"        mul r4.xyz, r1.xyzz, c[8 + a0].xyzz",
"      + adds r5.w, r0.xz",
"        cos r6.w, r0.x",
"        adds r5.w, r0.zx",
"        jmp l5",
"ccall b1, l5",
"nop",
"        label l5",
"(!p0)        exec",
"cexec b5, Yield=true",
"cexec !b6",
"        mulsc r3.w, c1.z, r1.w",
"loop i7, L4",
"   label L3",
"   exec",
"   setp_eq r15, c[aL].w",
"   (!p0) add r0, r0, c[aL]",
"(p0) endloop i7, L3",
"label L4",
"exece",
"        mulsc r3.w, c3.z, r6.x",
"        mulsc r3.w, c200.z, r31.x",
"        mov oDepth.x, c3.w",
"        cnop",
        });
    }

    class NopIncludeHandler : CompilerIncludeHandler {
      public override Stream Open(CompilerIncludeHandlerType includeType,
                             string filename) {
        throw new NotImplementedException();
      }
    }

    void Process(string shaderSourceCode) {
      shaderSourceCode += "\ncnop";
      shaderSourceCode += "\ncnop";
      var preprocessorDefines = new CompilerMacro[2];
      preprocessorDefines[0].Name = "XBOX";
      preprocessorDefines[0].Name = "XBOX360";
      var includeHandler = new NopIncludeHandler();
      var options = CompilerOptions.None;
      var compiledShader = ShaderCompiler.AssembleFromSource(
          shaderSourceCode, preprocessorDefines, includeHandler, options,
          Microsoft.Xna.Framework.TargetPlatform.Xbox360);

      var disassembledSourceCode = compiledShader.ErrorsAndWarnings;
      disassembledSourceCode = disassembledSourceCode.Replace("\n", "\r\n");
      if (disassembledSourceCode.IndexOf("// PDB hint 00000000-00000000-00000000") == -1) {
        outputTextBox.Text = disassembledSourceCode;
        compilerUcodeTextBox.Text = "";
        wordsTextBox.Text = "";
        return;
      }
      var prefix = disassembledSourceCode.Substring(
          0, disassembledSourceCode.IndexOf(
                 ':', disassembledSourceCode.IndexOf(':') + 1));
      disassembledSourceCode =
          disassembledSourceCode.Replace(prefix + ": ", "");
      disassembledSourceCode = disassembledSourceCode.Replace(
          "// PDB hint 00000000-00000000-00000000\r\n", "");
      var firstLine = disassembledSourceCode.IndexOf("//");
      var warnings = "// " +
                     disassembledSourceCode.Substring(0, firstLine)
                         .Replace("\r\n", "\r\n// ");
      disassembledSourceCode =
          warnings + disassembledSourceCode.Substring(firstLine + 3);
      disassembledSourceCode = disassembledSourceCode.Trim();
      outputTextBox.Text = disassembledSourceCode;

      string shaderType =
          shaderSourceCode.IndexOf("xvs_") == -1 ? "ps" : "vs";
      var ucodeWords = ExtractAndDumpWords(shaderType, compiledShader.GetShaderCode());
      if (ucodeWords != null) {
        TryCompiler(shaderType, ucodeWords);
      } else {
        compilerUcodeTextBox.Text = "";
      }

      if (compilerUcodeTextBox.Text.Length > 0) {
        var sourcePrefix = disassembledSourceCode.Substring(0, disassembledSourceCode.IndexOf("/*"));
        TryRoundTrip(sourcePrefix, compilerUcodeTextBox.Text, compiledShader.GetShaderCode());
      }
    }

    void TryCompiler(string shaderType, uint[] ucodeWords) {
      string ucodePath = Path.Combine(Path.GetTempPath(), "shader_playground_ucode.bin." + shaderType);
      string ucodeDisasmPath = Path.Combine(Path.GetTempPath(), "shader_playground_disasm.ucode.txt");
      string spirvDisasmPath = Path.Combine(Path.GetTempPath(), "shader_playground_disasm.spirv.txt");
      if (File.Exists(ucodePath)) {
        File.Delete(ucodePath);
      }
      if (File.Exists(ucodeDisasmPath)) {
        File.Delete(ucodeDisasmPath);
      }
      if (File.Exists(spirvDisasmPath)) {
        File.Delete(spirvDisasmPath);
      }

      byte[] ucodeBytes = new byte[ucodeWords.Length * 4];
      Buffer.BlockCopy(ucodeWords, 0, ucodeBytes, 0, ucodeWords.Length * 4);
      File.WriteAllBytes(ucodePath, ucodeBytes);

      if (!File.Exists(compilerPath_)) {
        compilerUcodeTextBox.Text = "Compiler not found: " + compilerPath_;
        return;
      }

      var startInfo = new ProcessStartInfo(compilerPath_);
      startInfo.Arguments = string.Join(" ", new string[]{
        "--shader_input=" + ucodePath,
        "--shader_input_type=" + shaderType,
        "--shader_output=" + ucodeDisasmPath,
        "--shader_output_type=ucode",
      });
      startInfo.WindowStyle = ProcessWindowStyle.Hidden;
      startInfo.CreateNoWindow = true;
      try {
        using (var process = System.Diagnostics.Process.Start(startInfo)) {
          process.WaitForExit();
        }
        string disasmText = File.ReadAllText(ucodeDisasmPath);
        compilerUcodeTextBox.Text = disasmText;
      } catch {
        compilerUcodeTextBox.Text = "COMPILER FAILURE";
      }

      startInfo = new ProcessStartInfo(compilerPath_);
      startInfo.Arguments = string.Join(" ", new string[]{
        "--shader_input=" + ucodePath,
        "--shader_input_type=" + shaderType,
        "--shader_output=" + spirvDisasmPath,
        "--shader_output_type=spirvtext",
      });
      startInfo.WindowStyle = ProcessWindowStyle.Hidden;
      startInfo.CreateNoWindow = true;
      try {
        using (var process = System.Diagnostics.Process.Start(startInfo)) {
          process.WaitForExit();
        }
        string disasmText = File.ReadAllText(spirvDisasmPath);
        compilerTranslatedTextBox.Text = disasmText;
      } catch {
        compilerTranslatedTextBox.Text = "COMPILER FAILURE";
      }
    }

    void TryRoundTrip(string sourcePrefix, string compilerSource, byte[] expectedBytes) {
      var shaderSourceCode = sourcePrefix + compilerSource;
      var preprocessorDefines = new CompilerMacro[2];
      preprocessorDefines[0].Name = "XBOX";
      preprocessorDefines[0].Name = "XBOX360";
      var includeHandler = new NopIncludeHandler();
      var options = CompilerOptions.None;
      var compiledShader = ShaderCompiler.AssembleFromSource(
          shaderSourceCode, preprocessorDefines, includeHandler, options,
          Microsoft.Xna.Framework.TargetPlatform.Xbox360);
      var compiledBytes = compiledShader.GetShaderCode();
      if (compiledBytes == null ||
          compiledBytes.Length != expectedBytes.Length ||
          !MemCmp(compiledBytes, expectedBytes)) {
        compilerUcodeTextBox.BackColor = System.Drawing.Color.Red;
      } else {
        compilerUcodeTextBox.BackColor = System.Drawing.SystemColors.Control;
      }
    }

    bool MemCmp(byte[] a1, byte[] b1) {
      if (a1 == null || b1 == null) {
        return false;
      }
      int length = a1.Length;
      if (b1.Length != length) {
        return false;
      }
      while (length > 0) {
        length--;
        if (a1[length] != b1[length]) {
          return false;
        }
      }
      return true;
    }

    uint[] ExtractAndDumpWords(string shaderType, byte[] shaderCode) {
      if (shaderCode == null || shaderCode.Length == 0) {
        wordsTextBox.Text = "";
        return null;
      }

      // Find shader code.
      int byteOffset = (shaderCode[4] << 24) | (shaderCode[5] << 16) |
                       (shaderCode[6] << 8) | (shaderCode[7] << 0);
      int wordOffset = byteOffset / 4;

      uint[] swappedCode = new uint[(shaderCode.Length - wordOffset) / sizeof(uint)];
      Buffer.BlockCopy(shaderCode, wordOffset * 4, swappedCode, 0, shaderCode.Length - wordOffset * 4);
      for (int i = 0; i < swappedCode.Length; ++i) {
        swappedCode[i] = SwapBytes(swappedCode[i]);
      }
      var sb = new StringBuilder();
      sb.Append("const uint32_t shader_dwords[] = {");
      for (int i = 0; i < swappedCode.Length; ++i) {
        sb.AppendFormat("0x{0:X8}, ", swappedCode[i]);
      }
      sb.Append("};\r\n");
      sb.Append("shader_type = ShaderType::" + (shaderType == "vs" ? "kVertex" : "kPixel") + ";\r\n");
      wordsTextBox.Text = sb.ToString();
      wordsTextBox.SelectAll();

      return swappedCode;
    }

    uint SwapBytes(uint x) {
      return ((x & 0x000000ff) << 24) +
             ((x & 0x0000ff00) << 8) +
             ((x & 0x00ff0000) >> 8) +
             ((x & 0xff000000) >> 24);
    }
  }
}
