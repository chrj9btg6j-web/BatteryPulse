using System;
using BatteryPulse;

internal static class CodexUsageReaderTests
{
    private static int assertions;

    private static int Main()
    {
        try
        {
            ParsesBothWindowsRegardlessOfOrder();
            SelectsOnlyTheCodexBucket();
            FallsBackToTheLegacyBucketOnlyWhenMapIsAbsent();
            RejectsMissingOrInvalidQuotaData();
            FormatsOnlyFreshUnexpiredValues();
            BoundsRetryDelays();
            Console.WriteLine("Passed " + assertions + " Codex usage checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Test failure after " + assertions + " checks: " + ex.GetType().FullName + " " + ex.Message);
            return 1;
        }
    }

    private static void ParsesBothWindowsRegardlessOfOrder()
    {
        CodexUsageSnapshot snapshot = Parse(Response(
            "\"primary\":{\"windowDurationMins\":10080,\"usedPercent\":45.8,\"resetsAt\":2000000000}," +
            "\"secondary\":{\"windowDurationMins\":300,\"usedPercent\":15,\"resetsAt\":2000000100}"));
        Equal(85.0, snapshot.FiveHourRemainingPercent.Value, "five hour remaining");
        Equal(54.2, snapshot.WeeklyRemainingPercent.Value, "weekly remaining");
        Equal("85", snapshot.FormatFiveHour(Utc(1900000000)), "five hour display floors");
        Equal("54", snapshot.FormatWeekly(Utc(1900000000)), "weekly display floors");
    }

    private static void SelectsOnlyTheCodexBucket()
    {
        string json = "{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":1,\"resetsAt\":2000000000}}," +
            "\"rateLimitsByLimitId\":{\"codex_other\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":3,\"resetsAt\":2000000000}}," +
            "\"codex\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":28,\"resetsAt\":2000000000}," +
            "\"secondary\":{\"windowDurationMins\":10080,\"usedPercent\":9,\"resetsAt\":2000000100}}}}}";
        CodexUsageSnapshot snapshot = Parse(json);
        Equal(72.0, snapshot.FiveHourRemainingPercent.Value, "codex five hour value");
        Equal(91.0, snapshot.WeeklyRemainingPercent.Value, "codex weekly value");

        bool parsed = CodexUsageReader.TryParseResponse(
            "{\"id\":2,\"result\":{\"rateLimitsByLimitId\":{\"codex_other\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":3,\"resetsAt\":2000000000}}}}}",
            Utc(1900000000), out snapshot);
        IsFalse(parsed, "missing codex bucket must not fall back to other limits");
    }

    private static void FallsBackToTheLegacyBucketOnlyWhenMapIsAbsent()
    {
        CodexUsageSnapshot snapshot = Parse("{\"id\":2,\"result\":{\"rateLimits\":{" +
            "\"primary\":{\"windowDurationMins\":300,\"usedPercent\":20,\"resetsAt\":2000000000}," +
            "\"secondary\":{\"windowDurationMins\":10080,\"usedPercent\":30,\"resetsAt\":2000000100}}}}");
        Equal(80.0, snapshot.FiveHourRemainingPercent.Value, "legacy five hour window");
        Equal(70.0, snapshot.WeeklyRemainingPercent.Value, "legacy weekly window");
    }

    private static void RejectsMissingOrInvalidQuotaData()
    {
        CodexUsageSnapshot ignored;
        IsFalse(CodexUsageReader.TryParseResponse("not json", Utc(1900000000), out ignored), "malformed response");
        IsFalse(CodexUsageReader.TryParseResponse("{\"result\":{}}", Utc(1900000000), out ignored), "missing bucket");
        IsFalse(CodexUsageReader.TryParseResponse("{\"result\":{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":true,\"resetsAt\":2000000000}}}}", Utc(1900000000), out ignored), "boolean percentage");
        IsFalse(CodexUsageReader.TryParseResponse("{\"result\":{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":\"42\",\"resetsAt\":2000000000}}}}", Utc(1900000000), out ignored), "string percentage");
        IsTrue(CodexUsageReader.TryParseResponse("{\"result\":{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300,\"usedPercent\":42,\"resetsAt\":\"2000000000\"}}}}", Utc(1900000000), out ignored), "usage parses when optional reset is malformed");
        Equal("--", ignored.FormatFiveHour(Utc(1900000000)), "malformed reset hides display");

        CodexUsageSnapshot clamped = Parse(Response(
            "\"primary\":{\"windowDurationMins\":300,\"usedPercent\":-10,\"resetsAt\":2000000000}," +
            "\"secondary\":{\"windowDurationMins\":10080,\"usedPercent\":120,\"resetsAt\":2000000100}"));
        Equal(100.0, clamped.FiveHourRemainingPercent.Value, "remaining clamp high");
        Equal(0.0, clamped.WeeklyRemainingPercent.Value, "remaining clamp low");
    }

    private static void FormatsOnlyFreshUnexpiredValues()
    {
        CodexUsageSnapshot snapshot = Parse(Response(
            "\"primary\":{\"windowDurationMins\":300,\"usedPercent\":20,\"resetsAt\":1900001000}," +
            "\"secondary\":{\"windowDurationMins\":10080,\"usedPercent\":30,\"resetsAt\":1900002000}"), Utc(1900000000));
        Equal("80", snapshot.FormatFiveHour(Utc(1900000170)), "fresh before reset");
        Equal("--", snapshot.FormatFiveHour(Utc(1900000181)), "stale snapshot");
        Equal("--", snapshot.FormatFiveHour(Utc(1900001000)), "expired reset");
        Equal("--", snapshot.FormatWeekly(Utc(1899999900)), "clock skew");
        Equal("--", CodexUsageSnapshot.Unavailable.FormatFiveHour(Utc(1900000000)), "unavailable state");

        CodexUsageSnapshot missingReset = Parse(Response(
            "\"primary\":{\"windowDurationMins\":300,\"usedPercent\":20}," +
            "\"secondary\":{\"windowDurationMins\":10080,\"usedPercent\":30}"), Utc(1900000000));
        Equal("--", missingReset.FormatFiveHour(Utc(1900000000)), "missing reset is unavailable");
    }

    private static void BoundsRetryDelays()
    {
        Equal(60, CodexUsageReader.GetRetryDelaySeconds(0), "success refresh interval");
        Equal(120, CodexUsageReader.GetRetryDelaySeconds(1), "first failure backoff");
        Equal(240, CodexUsageReader.GetRetryDelaySeconds(2), "second failure backoff");
        Equal(480, CodexUsageReader.GetRetryDelaySeconds(3), "third failure backoff");
        Equal(900, CodexUsageReader.GetRetryDelaySeconds(4), "maximum backoff");
        Equal(900, CodexUsageReader.GetRetryDelaySeconds(99), "backoff remains bounded");
    }

    private static CodexUsageSnapshot Parse(string json)
    {
        return Parse(json, Utc(1900000000));
    }

    private static CodexUsageSnapshot Parse(string json, DateTime retrievedAtUtc)
    {
        CodexUsageSnapshot snapshot;
        IsTrue(CodexUsageReader.TryParseResponse(json, retrievedAtUtc, out snapshot), "response parses");
        return snapshot;
    }

    private static string Response(string bucketWindows)
    {
        return "{\"id\":2,\"result\":{\"rateLimitsByLimitId\":{\"codex\":{" + bucketWindows + "}}}}";
    }

    private static DateTime Utc(long unixSeconds)
    {
        return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds);
    }

    private static void IsTrue(bool value, string name)
    {
        assertions++;
        if (!value) throw new Exception("Failed: " + name);
    }

    private static void IsFalse(bool value, string name)
    {
        IsTrue(!value, name);
    }

    private static void Equal(double expected, double actual, string name)
    {
        assertions++;
        if (Math.Abs(expected - actual) > 0.000001) throw new Exception("Failed: " + name + "; expected " + expected + ", got " + actual);
    }

    private static void Equal(int expected, int actual, string name)
    {
        assertions++;
        if (expected != actual) throw new Exception("Failed: " + name + "; expected " + expected + ", got " + actual);
    }

    private static void Equal(string expected, string actual, string name)
    {
        assertions++;
        if (expected != actual) throw new Exception("Failed: " + name + "; expected " + expected + ", got " + actual);
    }
}
