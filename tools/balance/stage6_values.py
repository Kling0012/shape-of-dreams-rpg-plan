"""Stage 6 domains; resolve everything before the shared generator publishes."""
import loot_values
import boss_sets_values
import economy_values
import pact_values
import daily_dream_values
import waypoint_values
import events_values

DOMAINS = {
    "loot": loot_values,
    "bossSets": boss_sets_values,
    "economy": economy_values,
    "pacts": pact_values,
    "dailyDream": daily_dream_values,
    "waypoints": waypoint_values,
    "events": events_values,
}


def load_tables():
    return {name: module.load() for name, module in DOMAINS.items()}


def render_outputs():
    outputs = {}
    for module in DOMAINS.values():
        outputs.update(module.render_outputs())
    return outputs
