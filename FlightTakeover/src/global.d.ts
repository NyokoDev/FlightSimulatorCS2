declare module "*.scss";
declare module "*.css";
declare module "*.sass";

declare module "*.svg" {
    const src: string;
    export default src;
}

declare module "cs2/ui" {
    import type { ComponentType } from "react";

    export const Button: ComponentType<{
        src?: string;
        className?: string;
        variant?: string;
        onClick?: () => void;
        disabled?: boolean;
        children?: React.ReactNode;
    }>;
}