import { execFile } from "node:child_process";
import { promisify } from "node:util";

import { StreamedReadTestRunner } from "./TestRunner.ts";
import { TestFeatures } from "../../../variants/types.ts";
import type { TestVariant } from "../../../variants/types.ts";
import type { StreamedReadTestResult } from "../types.ts";

const execFileAsync = promisify(execFile);

function conformanceDll(): string {
  const dll = process.env.U2F_MCAP_CONFORMANCE_DLL;
  if (!dll) {
    throw new Error("U2F_MCAP_CONFORMANCE_DLL is not set");
  }
  return dll;
}

async function runCsharp(mode: string, filePath: string): Promise<string> {
  const { stdout, stderr } = await execFileAsync("dotnet", [conformanceDll(), mode, filePath], {
    maxBuffer: 64 * 1024 * 1024,
  });
  if (stderr.trim().length > 0) {
    process.stderr.write(stderr);
  }
  return stdout;
}

export default class CsharpStreamedReaderTestRunner extends StreamedReadTestRunner {
  readonly name = "csharp-streamed-reader";

  supportsVariant(variant: TestVariant): boolean {
    return !variant.features.has(TestFeatures.AddExtraDataToRecords);
  }

  async runReadTest(filePath: string): Promise<StreamedReadTestResult> {
    return JSON.parse(await runCsharp("read-streamed", filePath)) as StreamedReadTestResult;
  }
}
