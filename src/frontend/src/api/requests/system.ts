import { request } from "./http";
import type { SystemInfo } from "../types/System";

export const getSystemInfo = () => request<SystemInfo>("/api/system");
