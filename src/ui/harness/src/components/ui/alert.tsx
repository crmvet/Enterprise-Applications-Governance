// shadcn/ui alert (registry style new-york-v4, shadcn 4.23.0), MIT, with the product's tones added (info, warning,
// error, success) for panel failures and envelope warnings. role="alert" only for errors; the others are status.
import * as React from 'react';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '../../lib/utils';

const alertVariants = cva(
  'relative grid w-full grid-cols-[0_1fr] items-start gap-y-0.5 rounded-lg border px-3.5 py-2.5 text-sm has-[>svg]:grid-cols-[calc(var(--spacing)*4)_1fr] has-[>svg]:gap-x-2.5 [&>svg]:size-4 [&>svg]:translate-y-0.5 [&>svg]:text-current',
  {
    variants: {
      variant: {
        default: 'bg-card text-card-foreground',
        destructive: 'border-danger-line bg-danger-soft text-danger',
        error: 'border-danger-line bg-danger-soft text-foreground [&>svg]:text-danger',
        warning: 'border-warning-line bg-warning-soft text-foreground [&>svg]:text-warning',
        info: 'border-info-line bg-info-soft text-foreground [&>svg]:text-info',
        success: 'border-success-line bg-success-soft text-foreground [&>svg]:text-success',
      },
    },
    defaultVariants: {
      variant: 'default',
    },
  },
);

function Alert({ className, variant, role, ...props }: React.ComponentProps<'div'> & VariantProps<typeof alertVariants>) {
  const fallbackRole = variant === 'error' || variant === 'destructive' ? 'alert' : 'status';
  return <div data-slot="alert" role={role ?? fallbackRole} className={cn(alertVariants({ variant }), className)} {...props} />;
}

function AlertTitle({ className, ...props }: React.ComponentProps<'div'>) {
  return <div data-slot="alert-title" className={cn('col-start-2 min-h-4 font-semibold tracking-tight', className)} {...props} />;
}

function AlertDescription({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="alert-description"
      className={cn('col-start-2 grid justify-items-start gap-1 text-sm text-navy-2 [&_p]:leading-relaxed', className)}
      {...props}
    />
  );
}

export { Alert, AlertTitle, AlertDescription };
