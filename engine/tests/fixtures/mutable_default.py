"""Seeded bug: mutable default argument leaks state between calls.

``acc += [item]`` extends the shared default list in place and returns it, so
each call accumulates the previous calls' items. Written without attribute/
subscript access on the parameter so this fixture isolates the mutable-default
detector cleanly.
"""


def accumulate(item, acc=[]):
    acc += [item]
    return acc
