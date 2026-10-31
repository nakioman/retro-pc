import { useEffect, useState } from "react";
import { getSystemInfo } from "../api/requests/system";

const refreshIntervalMs = 30_000;

export function useDataFreeSpace(): number | null {
  const [freeBytes, setFreeBytes] = useState<number | null>(null);

  useEffect(() => {
    let active = true;
    const refresh = () =>
      getSystemInfo()
        .then((info) => {
          if (active) setFreeBytes(info.dataFreeBytes);
        })
        .catch(() => {
          if (active) setFreeBytes(null);
        });
    void refresh();
    const timer = window.setInterval(() => void refresh(), refreshIntervalMs);
    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, []);

  return freeBytes;
}

const mebibyte = 1024 * 1024;
const gibibyte = 1024 * mebibyte;

export function formatFreeSpace(bytes: number): string {
  if (bytes >= gibibyte) return `${(bytes / gibibyte).toFixed(1)} GB`;
  return `${Math.floor(bytes / mebibyte)} MB`;
}
