using System;
using System.Collections.Generic;
using Product.Engine.Configuration.Json;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Testing;

// Compares an expected JSON value with an actual one, as the fixture format describes (fixture.schema.json, payloadMatch).
// subset: an object matches when every field given matches, and other fields are ignored; an array matches when it
// has the same length and each item matches in order. exact: the values are equal.
public static class JsonMatch
{
    public static bool Matches(JsonTreeNode expected, JsonTreeNode actual, bool exact, string path, List<string> problems)
    {
        if (exact)
        {
            if (!expected.Equals(actual))
            {
                problems.Add(JsonPointer.Display(path) + ": expected " + JsonPointer.Show(expected) + ", got " + JsonPointer.Show(actual));
                return false;
            }

            return true;
        }

        if (expected.Kind == JsonTreeKind.Object)
        {
            if (actual.Kind != JsonTreeKind.Object)
            {
                problems.Add(JsonPointer.Display(path) + ": expected an object, got " + JsonPointer.Show(actual));
                return false;
            }

            bool ok = true;
            foreach (KeyValuePair<string, JsonTreeNode> member in expected.Members)
            {
                string here = JsonPointer.Append(path, member.Key);
                if (!actual.TryGetMember(member.Key, out JsonTreeNode? value))
                {
                    problems.Add(JsonPointer.Display(here) + ": missing");
                    ok = false;
                    continue;
                }

                ok &= Matches(member.Value, value, exact: false, here, problems);
            }

            return ok;
        }

        if (expected.Kind == JsonTreeKind.Array)
        {
            if (actual.Kind != JsonTreeKind.Array || actual.Items.Count != expected.Items.Count)
            {
                problems.Add(JsonPointer.Display(path) + ": expected an array of " + expected.Items.Count + " item(s), got " + JsonPointer.Show(actual));
                return false;
            }

            bool ok = true;
            for (int i = 0; i < expected.Items.Count; i++)
            {
                ok &= Matches(expected.Items[i], actual.Items[i], exact: false, JsonPointer.Append(path, i), problems);
            }

            return ok;
        }

        if (!expected.Equals(actual))
        {
            problems.Add(JsonPointer.Display(path) + ": expected " + JsonPointer.Show(expected) + ", got " + JsonPointer.Show(actual));
            return false;
        }

        return true;
    }
}
