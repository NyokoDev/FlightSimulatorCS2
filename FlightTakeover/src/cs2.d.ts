declare module "cs2/modding" { export type ModRegistrar = (registry: { append: (slot: string, component: React.ComponentType<any>) => void }) => void; }
declare module "cs2/api" { export function bindValue<T>(group: string, key: string, initial: T): unknown; export function useValue<T>(binding: unknown): T; export function trigger(group: string, key: string, ...args: unknown[]): void; }
