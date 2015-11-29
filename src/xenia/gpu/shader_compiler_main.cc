/**
 ******************************************************************************
 * Xenia : Xbox 360 Emulator Research Project                                 *
 ******************************************************************************
 * Copyright 2015 Ben Vanik. All rights reserved.                             *
 * Released under the BSD license - see LICENSE in the root for more details. *
 ******************************************************************************
 */

#include <gflags/gflags.h>

#include <cinttypes>
#include <string>
#include <vector>

#include "xenia/base/logging.h"
#include "xenia/base/main.h"
#include "xenia/base/string.h"
#include "xenia/gpu/shader_translator.h"

DEFINE_string(shader_input, "", "Input shader binary file path.");
DEFINE_string(shader_input_type, "",
              "'vs', 'ps', or unspecified to infer from the given filename.");
DEFINE_string(shader_output, "", "Output shader file path.");
DEFINE_string(shader_output_type, "ucode",
              "Translator to use: [ucode, spirvtext].");

namespace xe {
namespace gpu {

int shader_compiler_main(const std::vector<std::wstring>& args) {
  ShaderType shader_type;
  if (!FLAGS_shader_input_type.empty()) {
    if (FLAGS_shader_input_type == "vs") {
      shader_type = ShaderType::kVertex;
    } else if (FLAGS_shader_input_type == "ps") {
      shader_type = ShaderType::kPixel;
    } else {
      XELOGE("Invalid --shader_input_type; must be 'vs' or 'ps'.");
      return 1;
    }
  } else {
    auto last_dot = FLAGS_shader_input.find_last_of('.');
    bool valid_type = false;
    if (last_dot != std::string::npos) {
      if (FLAGS_shader_input.substr(last_dot) == ".vs") {
        shader_type = ShaderType::kVertex;
        valid_type = true;
      } else if (FLAGS_shader_input.substr(last_dot) == ".ps") {
        shader_type = ShaderType::kPixel;
        valid_type = true;
      }
    }
    if (!valid_type) {
      XELOGE(
          "File type not recognized (use .vs, .ps or "
          "--shader_input_type=vs|ps).");
      return 1;
    }
  }

  auto input_file = fopen(FLAGS_shader_input.c_str(), "rb");
  if (!input_file) {
    XELOGE("Unable to open input file: %s", FLAGS_shader_input.c_str());
    return 1;
  }
  fseek(input_file, 0, SEEK_END);
  size_t input_file_size = ftell(input_file);
  fseek(input_file, 0, SEEK_SET);
  std::vector<uint32_t> ucode_dwords(input_file_size / 4);
  fread(ucode_dwords.data(), 4, ucode_dwords.size(), input_file);
  fclose(input_file);

  XELOGI("Opened %s as a %s shader, %" PRId64 " words (%" PRId64 " bytes).",
         FLAGS_shader_input.c_str(),
         shader_type == ShaderType::kVertex ? "vertex" : "pixel",
         ucode_dwords.size(), ucode_dwords.size() * 4);

  std::unique_ptr<ShaderTranslator> translator;
  if (FLAGS_shader_output_type == "spirvtext") {
    // TODO(benvanik): SPIRV translator.
    translator = std::make_unique<UcodeShaderTranslator>();
  } else {
    translator = std::make_unique<UcodeShaderTranslator>();
  }

  // TODO(benvanik): hash? need to return the data to big-endian format first.
  uint64_t ucode_data_hash = 0;
  auto translated_shader = translator->Translate(
      shader_type, ucode_data_hash, ucode_dwords.data(), ucode_dwords.size());

  if (!FLAGS_shader_output.empty()) {
    auto output_file = fopen(FLAGS_shader_output.c_str(), "w");
    fwrite(translated_shader->binary().data(),
           translated_shader->binary().size(), 1, output_file);
    fclose(output_file);
  }

  return 0;
}

}  // namespace gpu
}  // namespace xe

DEFINE_ENTRY_POINT(L"xenia-gpu-shader-compiler",
                   L"xenia-gpu-shader-compiler shader.bin",
                   xe::gpu::shader_compiler_main);
