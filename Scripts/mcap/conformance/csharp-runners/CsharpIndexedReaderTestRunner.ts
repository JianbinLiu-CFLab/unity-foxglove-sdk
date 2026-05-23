import { execFile } from "node:child_process";
import { promisify } from "node:util";

import { IndexedReadTestRunner } from "./TestRunner.ts";
import { TestFeatures } from "../../../variants/types.ts";
import type { TestVariant } from "../../../variants/types.ts";
import type { IndexedReadTestResult } from "../types.ts";

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

export default class CsharpIndexedReaderTestRunner extends IndexedReadTestRunner {
  readonly name = "csharp-indexed-reader";

  supportsVariant({ records, features }: TestVariant): boolean {
    if (!records.some((record) => record.type === "Message")) {
      return false;
    }
    if (features.has(TestFeatures.AddExtraDataToRecords)) {
      return false;
    }
    return (
      features.has(TestFeatures.UseChunks) &&
      features.has(TestFeatures.UseChunkIndex) &&
      features.has(TestFeatures.UseMessageIndex) &&
      features.has(TestFeatures.UseRepeatedChannelInfos) &&
      features.has(TestFeatures.UseRepeatedSchemas)
    );
  }

  async runReadTest(filePath: string): Promise<IndexedReadTestResult> {
    return JSON.parse(await runCsharp("read-indexed", filePath)) as IndexedReadTestResult;
  }
}
