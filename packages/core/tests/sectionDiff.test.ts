import { describe, expect, it } from "vitest";

import {
  createSectionDiff,
  createSnapshotEnvelope,
  diffSnapshotSets
} from "../src/diff/sectionDiff";
import type {
  PageSectionWithHistory,
  SnapshotEnvelope,
  Uuid
} from "../src/types";

const PAGE_ID: Uuid = "page-0000-ffff";

function makeSection(
  overrides: Partial<PageSectionWithHistory> & Pick<PageSectionWithHistory, "id">
): PageSectionWithHistory {
  return {
    id: overrides.id,
    sectionKey: overrides.sectionKey ?? "hero",
    contentType: overrides.contentType ?? "Text",
    contentValue: overrides.contentValue ?? "Hello world",
    cssSelector: overrides.cssSelector ?? ".hero",
    mediaPath: overrides.mediaPath ?? null,
    mediaAltText: overrides.mediaAltText ?? null,
    displayOrder: overrides.displayOrder ?? 1,
    isLocked: overrides.isLocked ?? false,
    updatedAtUtc:
      overrides.updatedAtUtc ?? "2024-01-01T00:00:00.000Z",
    lastPublishedAtUtc:
      overrides.lastPublishedAtUtc ?? "2023-12-31T12:00:00.000Z",
    previousContentValue: overrides.previousContentValue,
    etag: overrides.etag
  };
}

function snapshot(section: PageSectionWithHistory, capturedAt: string): SnapshotEnvelope {
  return createSnapshotEnvelope(PAGE_ID, section, {
    capturedAtUtc: capturedAt,
    changeVersion: Date.parse(section.updatedAtUtc)
  });
}

describe("createSectionDiff", () => {
  it("marks newly introduced sections as added", () => {
    const added = snapshot(
      makeSection({
        id: "section-added",
        contentValue: "Fresh content"
      }),
      "2024-01-01T00:01:00.000Z"
    );

    const diff = createSectionDiff(null, added);

    expect(diff).not.toBeNull();
    expect(diff?.changeType).toBe("added");
    expect(diff?.annotations).toHaveLength(0);
    expect(diff?.after?.sectionId).toBe("section-added");
  });

  it("detects conflicts when previous content diverges from local baseline", () => {
    const baseline = snapshot(
      makeSection({
        id: "section-1",
        contentValue: "Local draft"
      }),
      "2024-01-01T00:00:30.000Z"
    );

    const remoteUpdate = snapshot(
      makeSection({
        id: "section-1",
        contentValue: "Remote edit",
        previousContentValue: "Earlier baseline"
      }),
      "2024-01-01T00:00:40.000Z"
    );

    const diff = createSectionDiff(baseline, remoteUpdate, {
      detectConflicts: true
    });

    expect(diff).not.toBeNull();
    expect(diff?.changeType).toBe("modified");
    expect(diff?.conflict).toBe(true);
    expect(diff?.annotations.some((annotation) => annotation.code === "content.conflict")).toBe(
      true
    );
  });

  it("emits large delta annotations for sizeable edits", () => {
    const base = snapshot(
      makeSection({
        id: "section-2",
        contentValue: "short"
      }),
      "2024-01-01T00:00:00.000Z"
    );

    const enlarged = snapshot(
      makeSection({
        id: "section-2",
        contentValue: "x".repeat(1500)
      }),
      "2024-01-01T00:00:10.000Z"
    );

    const diff = createSectionDiff(base, enlarged);

    expect(diff).not.toBeNull();
    expect(diff?.changeType).toBe("modified");
    expect(diff?.annotations.some((annotation) => annotation.code === "content.delta.large")).toBe(
      true
    );
  });
});

describe("diffSnapshotSets", () => {
  it("collects added, removed, and modified snapshots", () => {
    const before = [
      snapshot(
        makeSection({
          id: "section-a",
          contentValue: "alpha"
        }),
        "2024-01-01T00:00:00.000Z"
      ),
      snapshot(
        makeSection({
          id: "section-b",
          contentValue: "bravo"
        }),
        "2024-01-01T00:00:05.000Z"
      )
    ];

    const after = [
      snapshot(
        makeSection({
          id: "section-a",
          contentValue: "alpha-updated"
        }),
        "2024-01-01T00:00:10.000Z"
      ),
      snapshot(
        makeSection({
          id: "section-c",
          contentValue: "charlie"
        }),
        "2024-01-01T00:00:06.000Z"
      )
    ];

    const diffs = diffSnapshotSets(before, after);

    const changeMap = Object.fromEntries(
      diffs.map((entry) => [entry.sectionId, entry.changeType])
    );

    expect(changeMap).toEqual({
      "section-a": "modified",
      "section-b": "removed",
      "section-c": "added"
    });
  });
});
