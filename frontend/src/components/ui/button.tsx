import React from 'react'

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'default' | 'outline' | 'ghost'
  size?: 'sm' | 'md' | 'lg'
  children: React.ReactNode
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className = '', variant = 'default', size = 'md', children, ...props }, ref) => {
    const baseClasses = 'inline-flex items-center justify-center rounded-md font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 disabled:opacity-50 disabled:pointer-events-none'
    
    const variants = {
      default: 'bg-blue-600 text-white hover:bg-blue-700 dark:bg-blue-700 dark:hover:bg-blue-800',
      outline: 'border border-gray-300 bg-transparent hover:bg-gray-100 dark:border-gray-600 dark:hover:bg-gray-700 text-gray-800 dark:text-gray-200',
      ghost: 'bg-transparent hover:bg-gray-100 dark:hover:bg-gray-800 text-gray-800 dark:text-gray-200'
    }
    
    const sizes = {
      sm: 'h-8 px-3 text-xs',
      md: 'h-10 py-2 px-4 text-sm',
      lg: 'h-12 px-6 text-base'
    }
    
    const variantClasses = variants[variant]
    const sizeClasses = sizes[size]
    
    return (
      <button
        className={`${baseClasses} ${variantClasses} ${sizeClasses} ${className}`}
        ref={ref}
        {...props}
      >
        {children}
      </button>
    )
  }
) 