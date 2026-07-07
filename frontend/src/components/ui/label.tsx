"use client"

import * as React from "react"

import { cn } from "@/lib/utils"

function Label({ className, ...props }: React.ComponentProps<"label">) {
  return (
    <label
      data-slot="label"
      className={cn(
        "flex items-center gap-2 text-sm leading-none font-medium select-none has-disabled:cursor-not-allowed has-disabled:text-muted-foreground has-data-disabled:cursor-not-allowed has-data-disabled:text-muted-foreground group-data-[disabled=true]:cursor-not-allowed group-data-[disabled=true]:text-muted-foreground group-data-[disabled=true]:opacity-100 peer-disabled:cursor-not-allowed peer-disabled:text-muted-foreground peer-disabled:opacity-100 peer-data-disabled:cursor-not-allowed peer-data-disabled:text-muted-foreground peer-data-disabled:opacity-100",
        className
      )}
      {...props}
    />
  )
}

export { Label }
