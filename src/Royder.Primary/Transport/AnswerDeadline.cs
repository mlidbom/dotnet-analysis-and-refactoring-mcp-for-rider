using System.Diagnostics;

namespace Royder.Primary.Transport;

/// <summary>The time by which the primary answers one tool call, whatever Rider is still doing for it.</summary>
/// <remarks>
///   MCP clients give up on a call after 60 seconds, and Claude Code then tells the agent only "The operation timed
///   out.": of 1,122 calls agents made through Claude Code by 2026-11-24, none answered after 49 seconds, and the four
///   that ran to 60 failed so. An agent told that much falls back to text search. The primary's own answer before
///   then says what Rider is still doing and what to do about it.
/// </remarks>
sealed class AnswerDeadline(TimeSpan within)
{
   readonly long _started = Stopwatch.GetTimestamp();

   public TimeSpan Within => within;

   /// <summary>The result of <paramref name="work"/>, or a <see cref="TimeoutException"/> once the deadline has passed.</summary>
   public Task<T> Awaited<T>(Task<T> work, CancellationToken cancel) => work.WaitAsync(Remaining, cancel);

   /// <summary><paramref name="work"/> finished, or a <see cref="TimeoutException"/> once the deadline has passed.</summary>
   public Task Awaited(Task work, CancellationToken cancel) => work.WaitAsync(Remaining, cancel);

   TimeSpan Remaining => TimeSpan.FromTicks(Math.Max(0, (within - Stopwatch.GetElapsedTime(_started)).Ticks));
}
