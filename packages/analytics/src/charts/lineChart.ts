import type { TrendPoint } from "../types";

const SVG_NS = "http://www.w3.org/2000/svg";
const DATE_FORMAT = new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric" });

interface TrendMeta {
  maxValue: number;
  points: { x: number; y: number; value: number; label: string }[];
}

function computeTrend(points: TrendPoint[], width: number, height: number): TrendMeta {
  const chartPoints = points.length ? points : [{ dayUtc: new Date().toISOString(), changes: 0, uniqueEditors: 0 } as TrendPoint];
  const normalized = chartPoints.map((point) => ({
    date: new Date(point.dayUtc),
    value: point.changes
  }));

  const maxValue = Math.max(1, ...normalized.map((entry) => entry.value));
  const actualWidth = Math.max(width, 1);
  const step = normalized.length > 1 ? actualWidth / (normalized.length - 1) : 0;

  const computed = normalized.map((entry, index) => ({
    x: index * step,
    y: maxValue === 0 ? 0 : 1 - entry.value / maxValue,
    value: entry.value,
    label: DATE_FORMAT.format(entry.date)
  }));

  return {
    maxValue,
    points: computed.map((entry) => ({
      x: entry.x,
      y: entry.y,
      value: entry.value,
      label: entry.label
    }))
  };
}

function makeSvg(width: number, height: number): SVGSVGElement {
  const svg = document.createElementNS(SVG_NS, "svg");
  svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
  svg.setAttribute("preserveAspectRatio", "xMidYMid meet");
  svg.setAttribute("role", "img");
  svg.classList.add("insights-analytics__svg");
  return svg;
}

function createPath(d: string, className: string): SVGPathElement {
  const path = document.createElementNS(SVG_NS, "path");
  path.setAttribute("d", d);
  path.setAttribute("class", className);
  return path;
}

function createCircle(x: number, y: number, value: number, label: string): SVGCircleElement {
  const circle = document.createElementNS(SVG_NS, "circle");
  circle.setAttribute("cx", x.toFixed(2));
  circle.setAttribute("cy", y.toFixed(2));
  circle.setAttribute("r", value > 0 ? "3.5" : "2");
  circle.setAttribute("data-value", value.toString(10));
  circle.setAttribute("data-label", label);
  circle.classList.add("insights-analytics__point");
  return circle;
}

export function renderTrendChart(host: HTMLElement, trend: TrendPoint[]): void {
  host.innerHTML = "";

  const margin = { top: 16, right: 16, bottom: 32, left: 40 };
  const width = 640;
  const height = 240;
  const chartWidth = width - margin.left - margin.right;
  const chartHeight = height - margin.top - margin.bottom;

  const { points, maxValue } = computeTrend(trend, chartWidth, chartHeight);

  if (!trend.length || maxValue === 0) {
    const empty = document.createElement("p");
    empty.className = "insights-analytics__empty";
    empty.textContent = "No activity matched the current filters.";
    host.appendChild(empty);
    return;
  }

  const svg = makeSvg(width, height);
  const group = document.createElementNS(SVG_NS, "g");
  group.setAttribute("transform", `translate(${margin.left}, ${margin.top})`);
  svg.appendChild(group);

  const baseline = chartHeight;
  const pathPoints = points.map((point) => {
    const x = point.x;
    const y = chartHeight * point.y;
    return { ...point, x, y };
  });

  const lineD = pathPoints
    .map((point, index) => `${index === 0 ? "M" : "L"}${point.x.toFixed(2)} ${point.y.toFixed(2)}`)
    .join(" ");

  const areaD = [
    `M${pathPoints[0].x.toFixed(2)} ${baseline.toFixed(2)}`,
    ...pathPoints.map((point) => `L${point.x.toFixed(2)} ${point.y.toFixed(2)}`),
    `L${pathPoints[pathPoints.length - 1].x.toFixed(2)} ${baseline.toFixed(2)}`,
    "Z"
  ].join(" ");

  group.appendChild(createPath(areaD, "insights-analytics__area"));
  group.appendChild(createPath(lineD, "insights-analytics__line"));

  pathPoints.forEach((point) => {
    const circle = createCircle(point.x, point.y, point.value, point.label);
    group.appendChild(circle);
  });

  // Horizontal grid line at half-way point for quick visual reference
  const midValue = Math.max(1, Math.round(maxValue / 2));
  const grid = document.createElementNS(SVG_NS, "line");
  grid.setAttribute("x1", "0");
  grid.setAttribute("x2", chartWidth.toFixed(2));
  grid.setAttribute("y1", (chartHeight - (chartHeight * (midValue / maxValue))).toFixed(2));
  grid.setAttribute("y2", (chartHeight - (chartHeight * (midValue / maxValue))).toFixed(2));
  grid.setAttribute("class", "insights-analytics__grid");
  group.appendChild(grid);

  const axis = document.createElementNS(SVG_NS, "line");
  axis.setAttribute("x1", "0");
  axis.setAttribute("x2", chartWidth.toFixed(2));
  axis.setAttribute("y1", baseline.toFixed(2));
  axis.setAttribute("y2", baseline.toFixed(2));
  axis.setAttribute("class", "insights-analytics__axis");
  group.appendChild(axis);

  const labels = document.createElementNS(SVG_NS, "g");
  labels.setAttribute("class", "insights-analytics__labels");

  const labelIndices = new Set<number>();
  labelIndices.add(0);
  if (pathPoints.length > 2) {
    labelIndices.add(Math.floor((pathPoints.length - 1) / 2));
  }
  labelIndices.add(pathPoints.length - 1);

  labelIndices.forEach((index) => {
    const point = pathPoints[index];
    const text = document.createElementNS(SVG_NS, "text");
    text.textContent = points[index].label;
    text.setAttribute("x", point.x.toFixed(2));
    text.setAttribute("y", (baseline + 20).toFixed(2));
    text.setAttribute("text-anchor", "middle");
    labels.appendChild(text);
  });

  group.appendChild(labels);
  host.appendChild(svg);
}
