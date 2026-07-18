"""Seeded smell: catching ``Exception`` hides unexpected errors."""


def load(value):
    try:
        return int(value)
    except Exception:
        return None
