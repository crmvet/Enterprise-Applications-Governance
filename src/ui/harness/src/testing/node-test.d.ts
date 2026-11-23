// The small part of Node's test runner and strict assert that the *.test.ts files use, typed here so the typecheck
// needs no @types/node package. scripts/run-tests.mjs bundles the tests with esbuild and runs them with node --test.
declare module 'node:test' {
  export function test(name: string, fn: () => void | Promise<void>): Promise<void>;
  export function describe(name: string, fn: () => void): void;
}

declare module 'node:assert/strict' {
  interface StrictAssert {
    (value: unknown, message?: string): void;
    ok(value: unknown, message?: string): void;
    equal(actual: unknown, expected: unknown, message?: string): void;
    notEqual(actual: unknown, expected: unknown, message?: string): void;
    deepEqual(actual: unknown, expected: unknown, message?: string): void;
    match(value: string, pattern: RegExp, message?: string): void;
    doesNotMatch(value: string, pattern: RegExp, message?: string): void;
    throws(fn: () => unknown, expected?: RegExp, message?: string): void;
  }
  const assert: StrictAssert;
  export default assert;
}
