// shadcn/ui badge (registry style new-york-v4, shadcn 4.23.0), MIT, with the product's tone variants added: a tinted
// fill, a matching line and strong text, the same tones the engine meanings map to (data/meanings.ts).
import * as React from 'react';
import { cva, type VariantProps } from 'class-variance-authority';
import { Slot } from 'radix-ui';
import { cn } from '../../lib/utils';

const badgeVariants = cva(
  "inline-flex w-fit shrink-0 items-center justify-center gap-1 overflow-hidden rounded-md border px-1.5 py-px text-xs leading-[18px] font-semibold whitespace-nowrap transition-[color,box-shadow] focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 [&>svg]:pointer-events-none [&>svg]:size-3 [&>svg]:shrink-0",
  {
    variants: {
      variant: {
        default: 'border-transparent bg-primary text-primary-foreground [a&]:hover:bg-primary/90',
        secondary: 'border-transparent bg-secondary text-secondary-foreground [a&]:hover:bg-secondary/90',
        destructive: 'border-transparent bg-destructive text-white [a&]:hover:bg-destructive/90',
        outline: 'border-border bg-card text-foreground [a&]:hover:bg-accent [a&]:hover:text-accent-foreground',
        success: 'border-success-line bg-success-soft text-success',
        warning: 'border-warning-line bg-warning-soft text-warning',
        danger: 'border-danger-line bg-danger-soft text-danger',
        brand: 'border-info-line bg-info-soft text-brand-dark',
        informative: 'border-info-line bg-info-soft text-info',
        subtle: 'border-subtle-line bg-subtle-soft text-navy-2',
      },
    },
    defaultVariants: {
      variant: 'default',
    },
  },
);

function Badge({
  className,
  variant = 'default',
  asChild = false,
  ...props
}: React.ComponentProps<'span'> & VariantProps<typeof badgeVariants> & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : 'span';
  return <Comp data-slot="badge" data-variant={variant} className={cn(badgeVariants({ variant }), className)} {...props} />;
}

export { Badge, badgeVariants };
