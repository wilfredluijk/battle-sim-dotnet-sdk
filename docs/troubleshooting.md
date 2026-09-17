# Timing and connections

`wrong_tick` means a command's tick differs from the server's current command
window. `late_command` means the tick matches, but its deadline has expired.
Neither error alone means that the WebSocket disconnected. The runner echoes the
tick from the observation; changing that number manually would apply an old
decision to a different state.

At 10 Hz, a new tick opens every 100 ms. An 80 ms deadline must accommodate both
network transit and decision work. A response arriving after the next window
opens can be rejected as `wrong_tick`, even when the decision itself was quick.

Inspect `Diagnostics.LastTiming`, `Overruns`, `SkippedTicks`, `DiscardedCommands`
and `RejectedCommands`. `OnTickTiming.ElapsedMs` measures callback and command
serialization time, not network round-trip time or time already spent queued.
An `OverBudget` value of false does not guarantee server acceptance.

Keep `OnTick`/`Decide` quick. Use cached data; avoid sleeps, network calls, disk I/O
and heavy console logging in callbacks. A synchronous callback cannot be forcibly
interrupted. The runtime continues reading frames independently, skips obsolete
queued observations while retaining their events, and suppresses computed
commands that have already expired or been superseded. It cannot make a 150 ms
decision meet an 80 ms deadline. A consistently slow bot can therefore produce
no accepted commands while its discarded-command counter grows.

The local expiry check starts at client receipt. It cannot recover time spent
in transit or guarantee that a command will arrive before the server advances.
For persistent network jitter, use a lower-latency connection or ask the operator
for slower pacing or a lockstep round. Raising the deadline alone does not keep
an old tick window open after the next tick starts.

The hunter example prints occasional overrun warnings and logs disconnect phase
and close code. Exhausted transport failures throw `IOException` with a sanitized
category or close code. `LastDisconnect` also retains the interrupted phase;
normal peer close descriptions remain server-supplied text, so avoid logging them
indiscriminately. The example exits unsuccessfully if no match completed or a
transport error occurred. An active ship is never automatically reconnected;
`ReconnectAttempts` only retries connections outside an active match.

RunOptions string formatting redacts the token and omits free-form connection
fields. This does not redact arbitrary custom JSON serialization or explicit
logging of `options.Token`; never log participant credentials.
