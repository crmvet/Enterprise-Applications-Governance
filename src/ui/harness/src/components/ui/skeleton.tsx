// shadcn/ui skeleton (registry style new-york-v4, shadcn 4.23.0), MIT. Copied as source; see src/components/ui/README.md.
import { cn } from "../../lib/utils"

function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="skeleton"
      className={cn("animate-pulse rounded-md bg-accent", className)}
      {...props}
    />
  )
}

export { Skeleton }
