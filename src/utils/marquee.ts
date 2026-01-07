export const DEFAULT_MARQUEE_MAX_LENGTH = 10;
export const DEFAULT_MARQUEE_SEPARATOR = '   ';
export const DEFAULT_MARQUEE_INTERVAL_MS = 1000;

export class Marquee {
	private position: number = 0;
	private text: string = '';
	private readonly maxLength: number;
	private readonly separator: string;
	private intervalId: NodeJS.Timeout | undefined;
	private updateCallback: (() => void | Promise<void>) | undefined;
	private intervalMs: number;

	constructor(
		maxLength: number = DEFAULT_MARQUEE_MAX_LENGTH,
		separator: string = DEFAULT_MARQUEE_SEPARATOR,
		intervalMs: number = DEFAULT_MARQUEE_INTERVAL_MS
	) {
		this.maxLength = maxLength;
		this.separator = separator;
		this.intervalMs = intervalMs;
	}

	setText(text: string): void {
		if (this.text !== text) {
			this.text = text;
			this.position = 0;
		}
	}

	getCurrentFrame(): string {
		if (!this.text || this.text.length <= this.maxLength) {
			return this.text;
		}

		const extendedText = this.text + this.separator + this.text;
		const endPosition = this.position + this.maxLength;
		const frame = extendedText.substring(this.position, endPosition);

		this.position = (this.position + 1) % (this.text.length + this.separator.length);

		return frame;
	}

	start(callback: () => void | Promise<void>): void {
		this.stop();
		this.updateCallback = callback;
		this.intervalId = setInterval(() => {
			if (this.updateCallback) {
				this.updateCallback();
			}
		}, this.intervalMs);
	}

	stop(): void {
		if (this.intervalId) {
			clearInterval(this.intervalId);
			this.intervalId = undefined;
		}
		this.updateCallback = undefined;
	}

	reset(): void {
		this.position = 0;
	}

	getText(): string {
		return this.text;
	}

	isRunning(): boolean {
		return this.intervalId !== undefined;
	}

	setInterval(intervalMs: number): void {
		this.intervalMs = intervalMs;
		if (this.isRunning() && this.updateCallback) {
			const callback = this.updateCallback;
			this.stop();
			this.start(callback);
		}
	}

	getInterval(): number {
		return this.intervalMs;
	}
}

export function createMarquee(
	maxLength: number = DEFAULT_MARQUEE_MAX_LENGTH,
	separator: string = DEFAULT_MARQUEE_SEPARATOR,
	intervalMs: number = DEFAULT_MARQUEE_INTERVAL_MS
): Marquee {
	return new Marquee(maxLength, separator, intervalMs);
}

