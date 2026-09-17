# Hosted server review: last three matches, 17 September 2026

Reviewed the three most recent live matches using their persisted replay inputs,
post-match diagnostics, and corresponding container logs. Times below are
Europe/Amsterdam (CEST, UTC+02:00). Each match had six bots, ran at 10 Hz, and used
an 80 ms command deadline.

## Results

| Start | Duration | Result | In-match disconnects |
| --- | --- | --- | --- |
| 17:52:20 | 190.2 s / 1,902 ticks | Operator aborted | 3 |
| 18:11:33 | 24.7 s / 247 ticks | player05 won; last survivor | 0 |
| 18:12:48 | 50.1 s / 501 ticks | player01 won; last survivor | 0 |

The latest two matches finished normally. The oldest was deliberately aborted,
not terminated by a server crash. During that match, player09 closed its
connection at tick 4. The server disconnected player05 at tick 76 and player03 at
tick 326 after their protocol-violation limits were exceeded. The retained INFO
logs do not identify the complete sequence of individual violations.

The running container was healthy and had zero automatic restarts. The inspected
container-log interval contained no ERROR entries and one WARN: an attempted join
while the room was running. These log-level counts do not imply zero command
rejections; the persisted diagnostics below record those separately.

## Command delivery

Aggregated across all six clients in each match:

| Start | Accepted | Late | Wrong tick | Other rejected | Missed response windows |
| --- | ---: | ---: | ---: | ---: | ---: |
| 17:52:20 | 3,353 | 149 | 783 | 13 | 977 / 4,330 (22.56%) |
| 18:11:33 | 1,012 | 15 | 22 | 0 | 37 / 1,049 (3.53%) |
| 18:12:48 | 1,702 | 23 | 43 | 0 | 68 / 1,770 (3.84%) |

Missed windows count opportunities without an accepted command. Rejection counts
are events and must not be added to the missed-window count as independent lost
ticks. Per-client windows stop when that client's participation ends.

Command delivery improved substantially after the aborted match, although late
and wrong-tick commands remained. The older match also recorded response-time
95th percentiles above the 80 ms deadline for multiple clients, and large RTT
outliers. These observations warrant investigating client processing delays,
queued stale observations, and network jitter; they do not isolate one cause.

## .NET client observation

The registration log identifies player04 in the 18:11:33 match as
`naval-sdk-dotnet/0.1.0`. Its persisted diagnostics show:

- 49 accepted commands, 1 late command, 2 wrong-tick commands, no other rejections.
- 3 missed windows out of 52 completed windows.
- Response-time median 11.44 ms; 95th percentile 37.89 ms.
- 3 successful shots, activation requests for both selected powerups, and normal
  elimination.
- No in-match disconnect or forfeit.

No .NET client was registered in the other two matches. This confirms live
interoperability for the observed match; it is not evidence that the .NET SDK
caused the older clients' violations or that all network conditions are covered.

## Replay integrity and scope

All three replay files parsed successfully and contained a terminal end record.
There were no duplicate per-bot commands within a recorded tick or nonfinite
throttle/rudder values. The server intentionally omits ticks with no applied
commands, so gaps in the older replay are not evidence of file corruption.
Fire entries in replay inputs are requests; successful-shot counts above come
from the authoritative match report.

Additional `match started` entries between the live matches were replay
reconstructions: their bot registrations used `version="replay"`. They were
excluded from the three live runs reviewed here.

Reference replay IDs:

- `match_main_1789660340953783703_1_1`
- `match_main_1789661493797660899_1_6`
- `match_main_1789661568083026030_1_8`

This review publishes aggregate findings only. Credentials, peer addresses,
raw logs, replay seeds, and full replay files are not included. No production
configuration, participant state, or running match was changed during the review.
