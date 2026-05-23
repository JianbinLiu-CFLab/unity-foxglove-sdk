import { execFile } from "node:child_process";
import { promisify } from "node:util";

import { WriteTestRunner } from "./TestRunner.ts";
import type { TestVariant } from "../../../variants/types.ts";

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

  supportsVariant(_variant: TestVariant): boolean {
    return false;
  }

  async runWriteTest(filePath: string, _variant: TestVariant): Promise<Uint8Array> {
    await execFileAsync("dotnet", [conformanceDll(), "write", filePath], {
      maxBuffer: 64 * 1024 * 1024,
    });
    throw new Error("C# writer parity is deferred to Phase 122 and should be skipped.");
  }
}
