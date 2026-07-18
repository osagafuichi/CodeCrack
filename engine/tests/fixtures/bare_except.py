"""Seeded smell: a bare ``except:`` swallows every exception."""


def load(value):
    try:
        return int(value)
    except:  # noqa: E722 - the seeded smell under test
        return None
