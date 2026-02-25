import type { JSX } from "preact";
import "../styles/tokens.css";
import "../styles/data-table.css";

export interface DataTableColumn<Row> {
  id: string;
  header: string;
  width?: string;
  render: (row: Row) => JSX.Element | string;
}

export interface DataTableProps<Row> {
  columns: Array<DataTableColumn<Row>>;
  rows: Row[];
  getRowKey: (row: Row, index: number) => string;
  emptyMessage?: string;
}

export function DataTable<Row>({ columns, rows, getRowKey, emptyMessage = "No data available." }: DataTableProps<Row>) {
  return (
    <div className="bugence-table__wrapper">
      <table className="bugence-table">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.id} style={column.width ? { width: column.width } : undefined}>
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length} className="bugence-table__empty">
                {emptyMessage}
              </td>
            </tr>
          ) : (
            rows.map((row, index) => (
              <tr key={getRowKey(row, index)}>
                {columns.map((column) => (
                  <td key={column.id}>{column.render(row)}</td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

export default DataTable;
