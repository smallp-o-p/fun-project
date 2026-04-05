using System;

public static class Assert
{
    public static void That(bool condition, string msg = "")
    {
        if (!condition) throw new Exception($"Expected true but got false. {msg}");
    }

    public static void True(bool condition, string msg = "") => That(condition, msg);

    public static void False(bool condition, string msg = "")
    {
        if (condition) throw new Exception($"Expected false but got true. {msg}");
    }

    public static void Equal<T>(T real, T expect, string msg = "")
    {
        if (!real.Equals(expect)) throw new Exception($"Expected {expect} but got {real}. {msg}");
    }

    public static void Throws<T>(Action body, string msg = "") where T : Exception
    {
        try { body(); } catch (Exception e) when (e is T) { return; }
        throw new Exception($"Expected {typeof(T).Name} but no exception was thrown. {msg}");
    }
}
