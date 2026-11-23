#!/usr/bin/env node
// Stands in for the Azure CLI in the local test, so the Azure DevOps path runs without a real tenant.
// The test puts a small az wrapper that calls this file first on a PATH that holds no real Azure CLI.
//   FAKE_AZ_LOG    a file: each call appends its arguments as one JSON line
//   FAKE_AZ_TOKEN  the token to print
//   FAKE_AZ_FAIL   "1" makes the call fail as an Azure CLI that is not signed in
import fs from 'node:fs';

const args = process.argv.slice(2);
if (process.env.FAKE_AZ_LOG) fs.appendFileSync(process.env.FAKE_AZ_LOG, `${JSON.stringify(args)}\n`);
if (process.env.FAKE_AZ_FAIL === '1') {
  process.stderr.write("ERROR: Please run 'az login' to setup account. (fake az)\n");
  process.exit(1);
}
if (args[0] !== 'account' || args[1] !== 'get-access-token') {
  process.stderr.write(`fake az: unexpected command ${args.join(' ')}\n`);
  process.exit(2);
}
process.stdout.write(`${process.env.FAKE_AZ_TOKEN || ''}\n`);
