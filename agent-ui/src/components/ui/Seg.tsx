interface Option<T extends string> {
  value: T
  label: string
}

interface Props<T extends string> {
  value: T
  options: Option<T>[]
  onChange: (v: T) => void
  'aria-label': string
}

/** Counterpart of web/src/components/ui/Seg.tsx — a pill switch between two or more views of the
 *  same thing (here: list or grid). The states live in ui.css. */
export function Seg<T extends string>({ value, options, onChange, ...rest }: Props<T>) {
  return (
    <div role="group" aria-label={rest['aria-label']} className="sl-seg">
      {options.map(o => (
        <button key={o.value} type="button" aria-pressed={o.value === value} onClick={() => onChange(o.value)}>
          {o.label}
        </button>
      ))}
    </div>
  )
}
