"""Seeded bug: attribute access on a parameter that can be None (AttributeError)."""


def display_name(user):
    return user.name
