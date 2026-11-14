# Project Guidelines

These guidelines apply to all agents and contributors working in this repository.

## Design and Architecture

- Prioritize extensibility and ease of change in design decisions.
- Keep responsibilities clear and components loosely coupled. Prefer the simplest design that supports likely change; avoid speculative abstraction.
- Preserve the behavior and constraints defined in [spec.md](spec.md). When a change requires different behavior, update the specification as part of the same work unit.

## Dependencies and Tools

- Use only free libraries and tools whose licenses permit commercial use.
- Before adding a dependency or tool, verify its license from an authoritative source, including licenses of relevant transitive dependencies. Do not introduce software with unclear or non-commercial terms.
- Prefer existing project dependencies and tools when they meet the requirement.

## Documentation and Comments

- Write all documentation, source-code comments, and commit messages in English.
- Add concise comments proactively to explain design intent, non-obvious behavior, important constraints, and trade-offs. Do not add comments that merely repeat what the code already says.
- Keep documentation aligned with the implementation and update relevant documentation in the same work unit as behavior changes.

## Changes and Version Control

- Make a commit at each meaningful completed work-unit boundary, using a clear, specific commit message.
- Never push commits to a remote.
- Keep each change focused; do not include unrelated or pre-existing work in a commit.

## Validation

- Run the most relevant available checks for each change and report any checks that could not be run.
- Do not claim a change is verified unless the corresponding checks completed successfully.
