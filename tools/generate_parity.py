"""Generate a deterministic oracle using an installed original naval_sdk (0.5.0).

PYTHONPATH=/path/to/battle-sim-python-sdk python tools/generate_parity.py
The Python SDK's websockets dependency must also be installed.
"""
import copy
from dataclasses import asdict
import json
from pathlib import Path

from naval_sdk import Welcome, WorldView
from naval_sdk.tactical import (
    Tracker, Gunner, Helm, Evader, AlwaysActive, AlwaysPassive, DutyCycle,
    PingWhenStale, TacticalBot, Intent,
)

ROOT = Path(__file__).resolve().parents[1]
fixture = json.loads((ROOT / "tests/Naval.Sdk.Tests/Fixtures/protocol3.json").read_text())
welcome = Welcome.from_dict(fixture["welcome"])


class Hunter(TacticalBot):
    def __init__(self):
        super().__init__()
        self.sensor_policy = PingWhenStale()

    def decide(self, ctx):
        target = ctx.threats.nearest()
        return Intent.engage(target) if target else Intent.patrol((80, 80, 620, 620))


tracker = Tracker(welcome.ship_specs, tick_hz=60, simulation_dt=0.1)
gunner = Gunner(welcome.ship_specs)
helm = Helm(welcome.ship_specs)
evader = Evader()
policies = [AlwaysActive(), AlwaysPassive(), DutyCycle(), PingWhenStale()]
hunter = Hunter()
hunter.on_welcome(welcome)
cases = []
for tick in range(140):
    frame = copy.deepcopy(fixture["tick"])
    frame["tick"] = tick
    frame["self"].update(pos=[100 + tick * .1, 100], heading_deg=(tick * 7) % 360,
                         speed=9 if tick % 3 else -2, ammo=250 - tick // 15,
                         powerup_status=[dict(id="long_range_salvo", used=tick >= 30, active_ticks_left=max(0, 60-tick) if tick >= 30 else 0)])
    if tick < 70:
        frame["contacts"] = [dict(id=f"unstable-{tick}", kind="ship", pos=[200 + tick * .8, 180 + tick * .2],
                                  bearing_deg=125, range=130 if tick % 9 < 4 else None, confidence=.8)]
        if tick % 13 < 5:
            frame["contacts"].append(dict(id="neighbor", kind="ship", pos=[80, 300-tick*.2], bearing_deg=185, range=190, confidence=.7))
    if tick in (10, 28, 60):
        frame["events"] = [dict(type="hit", amount=5)]
    view = WorldView.from_dict(frame)
    tracks = tracker.update(view)
    gunner.update(view)
    solution = gunner.solve(view.me, tracks[0], view) if tracks else None
    evade = evader.update(view)
    cases.append(dict(view=frame, tracks=[asdict(t) for t in tracks],
                      solution=asdict(solution) if solution else None,
                      helm=helm.steer_to_point(view.me, (400, 200)),
                      sensors=[p.choose(view, tracker) for p in policies],
                      evade=evade.to_dict(tick, view.match_id) if evade else None,
                      command=hunter.on_tick(view).to_dict(tick, view.match_id)))
output = dict(source_commit="816fa9baba75c7294561a6f111aa2735f4bbdd01", cases=cases)
(ROOT / "tests/Naval.Sdk.Tests/Fixtures/python-parity.json").write_text(json.dumps(output, separators=(",", ":")) + "\n")
print(f"Generated {len(cases)} Python tactical oracle ticks.")
