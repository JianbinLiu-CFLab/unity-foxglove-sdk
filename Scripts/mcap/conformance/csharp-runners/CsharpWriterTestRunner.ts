import { execFile } from "node:child_process";
import { promisify } from "node:util";

import { WriteTestRunner } from "./TestRunner.ts";
import { TestFeatures, type TestVariant } from "../../../variants/types.ts";

const execFileAsync = promisify(execFile);

function conformanceDll(): string {
  const dll = process.env.U2F_MCAP_CONFORMANCE_DLL;
  if (!dll) {
    throw new Error("U2F_MCAP_CONFORMANCE_DLL is not set");
  }
  return dll;
}

export default class CsharpWriterTestRunner extends WriteTestRunner {
  readonly name = "csharp-writer";

  supportsVariant(variant: TestVariant): boolean {
    if (variant.features.has(TestFeatures.AddExtraDataToRecords)) {
      return false;
    }
    if (variant.features.has(TestFeatures.UseChunks)) {
      return false;
    }
    return true;
  }

  async runWriteTest(filePath: string, variant: TestVariant): Promise<Uint8Array> {
    const features = Array.from(variant.features).sort().join(",");
    const result = await execFileAsync("dotnet", [conformanceDll(), "write", filePath, features], {
      encoding: "buffer",
      maxBuffer: 64 * 1024 * 1024,
    });
    return new Uint8Array(result.stdout as Buffer);
  }
}
