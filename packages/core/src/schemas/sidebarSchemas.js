export const sidebarCards = {
    publishing_overview: {
        id: "publishing_overview",
        title: "Publishing Overview",
        description: "Track last publish and outstanding drafts.",
        metrics: [
            {
                id: "draft-count",
                label: "Draft sections",
                description: "Blocks updated since last publish.",
                icon: "fa-solid fa-pen-to-square",
                compute: "dynamic",
                valueField: "draftCount",
                formatter: "number",
                emphasis: "warning"
            },
            {
                id: "last-published",
                label: "Last publish",
                description: "Most recent publish timestamp.",
                icon: "fa-solid fa-rocket",
                compute: "dynamic",
                valueField: "lastPublishedAtUtc",
                formatter: "datetime"
            }
        ],
        actions: [
            {
                id: "open-publish-dialog",
                label: "Publish updates",
                icon: "fa-solid fa-rocket-launch",
                intent: "primary",
                target: "command",
                command: "canvas.publish"
            }
        ],
        footerHint: "Publishing requires review of all draft sections."
    },
    quality_checks: {
        id: "quality_checks",
        title: "Quality Checks",
        description: "Automated guardrails before shipping.",
        metrics: [
            {
                id: "accessibility-score",
                label: "Accessibility score",
                description: "Automated scan of current page.",
                icon: "fa-solid fa-universal-access",
                compute: "dynamic",
                valueField: "accessibilityScore",
                formatter: "percentage",
                emphasis: "default"
            },
            {
                id: "broken-links",
                label: "Broken links",
                icon: "fa-solid fa-link-slash",
                compute: "dynamic",
                valueField: "brokenLinkCount",
                formatter: "number",
                emphasis: "warning"
            }
        ],
        actions: [
            {
                id: "run-accessibility-scan",
                label: "Run scan",
                icon: "fa-solid fa-magnifying-glass-chart",
                target: "command",
                command: "canvas.scanAccessibility"
            }
        ]
    },
    quick_actions: {
        id: "quick_actions",
        title: "Quick Actions",
        actions: [
            {
                id: "open-history",
                label: "View history",
                icon: "fa-solid fa-wave-square",
                target: "drawer",
                command: "dashboard.openHistory"
            },
            {
                id: "schedule-reminder",
                label: "Schedule reminder",
                icon: "fa-solid fa-clock",
                target: "modal",
                command: "dashboard.scheduleReminder"
            },
            {
                id: "request-review",
                label: "Request review",
                icon: "fa-solid fa-user-check",
                target: "command",
                command: "canvas.requestReview"
            }
        ],
        footerHint: "Customize quick actions via admin settings."
    }
};
//# sourceMappingURL=sidebarSchemas.js.map