// shadcn/ui label (registry style new-york-v4, shadcn 4.23.0), MIT. Copied as source; see src/components/ui/README.md.
// Changed for the Academy palette: navy text.
import * as React from "react"
import { Label as LabelPrimitive } from "radix-ui"
import { cn } from "../../lib/utils"

function Label({ className, ...props }: React.ComponentProps<typeof LabelPrimitive.Root>) {
  return (
    <LabelPrimitive.Root
      data-slot="label"
      className={cn(
        "flex items-center gap-2 text-sm leading-none font-medium text-navy select-none group-data-[disabled=true]:pointer-events-none group-data-[disabled=true]:opacity-50 peer-disabled:cursor-not-allowed peer-disabled:opacity-50",
        className
      )}
      {...props}
    />
  )
}

export { Label }
