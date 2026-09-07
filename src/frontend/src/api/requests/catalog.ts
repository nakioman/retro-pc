import { request } from "./http";
import type { Catalog } from "../types/Catalog";

export const getCatalog = () => request<Catalog>("/api/catalog");
