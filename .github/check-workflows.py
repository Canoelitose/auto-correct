"""Checks that every workflow file still parses as YAML and has a name.

A broken workflow file does not show up as a failing build: GitHub prints the file path
instead of the workflow name and the run fails before any step, which reads like an
infrastructure hiccup rather than a mistake in the repository. Running the same check as an
ordinary CI step turns it back into a normal, obvious test failure.

Lives in a file rather than inline in the YAML, because a script embedded in the very file it
validates is exactly the kind of thing that breaks without anyone noticing.
"""

import glob
import sys

import yaml


def main() -> int:
    paths = sorted(glob.glob(".github/workflows/*.yml"))
    if not paths:
        print("no workflow files found - is the working directory the repository root?")
        return 1

    failed = False

    for path in paths:
        try:
            with open(path, encoding="utf-8") as handle:
                document = yaml.safe_load(handle)
        except yaml.YAMLError as error:
            print(f"{path}: does not parse: {error}")
            failed = True
            continue

        name = (document or {}).get("name")
        if not name:
            print(f"{path}: parses, but has no name")
            failed = True
            continue

        print(f"{path} -> {name}")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
