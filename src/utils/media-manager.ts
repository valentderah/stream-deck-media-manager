import { spawn, type ChildProcessWithoutNullStreams } from 'child_process';
import path from 'path';

export type MediaInfo = {
	Title?: string;
	Artist?: string;
	Artists?: string[];
	AlbumArtist?: string;
	AlbumTitle?: string;
	Status?: 'Playing' | 'Paused' | 'Stopped';
	CoverArtBase64?: string;
	CoverArtPart1Base64?: string;
	CoverArtPart2Base64?: string;
	CoverArtPart3Base64?: string;
	CoverArtPart4Base64?: string;
};

export type MediaManagerErrorType = 'FILE_NOT_FOUND' | 'HELPER_ERROR' | 'PARSING_ERROR' | 'NOTHING_PLAYING';

export type MediaManagerError = {
	type: MediaManagerErrorType;
	message: string;
	code?: string | number | null;
};

export type MediaManagerResult = 
	| { success: true; data: MediaInfo }
	| { success: false; error: MediaManagerError };

type MediaCommand = 'toggle' | 'next' | 'previous';

class MediaManagerService {
	private process: ChildProcessWithoutNullStreams | null = null;
	private subscribers: Set<(result: MediaManagerResult) => void> = new Set();
	private buffer: string = '';

	public subscribe(callback: (result: MediaManagerResult) => void): () => void {
		this.subscribers.add(callback);
		return () => {
			this.unsubscribe(callback);
		};
	}

	public unsubscribe(callback: (result: MediaManagerResult) => void): void {
		this.subscribers.delete(callback);

		if (this.subscribers.size === 0 && this.process) {
			this.process.kill();
			this.process = null;
		}
	}

	private notifySubscribers(result: MediaManagerResult): void {
		this.subscribers.forEach(callback => {
			try {
				callback(result);
			} catch (error) {
				console.error('Error in subscriber callback:', error);
			}
		});
	}

	public start(): void {
		if (this.process) return;

		const exeName = process.platform === 'win32' ? 'MediaManager.exe' : 'MediaManager';
		const managerPath = path.join(process.cwd(), 'bin', exeName);

		this.process = spawn(managerPath);

		this.process.stdout.on('data', (data: Buffer) => {
			this.buffer += data.toString();
			const lines = this.buffer.split('\n');
			this.buffer = lines.pop() || ''; // Keep the last, possibly incomplete, line

			for (const line of lines) {
				if (line.trim().length === 0) continue;
				try {
					const info: MediaInfo = JSON.parse(line);
					if (!info.Title && !info.Artist && (!info.Artists || info.Artists.length === 0)) {
						this.notifySubscribers({
							success: false,
							error: { type: 'NOTHING_PLAYING', message: 'No media data available' }
						});
					} else {
						this.notifySubscribers({ success: true, data: info });
					}
				} catch (parseError) {
					this.notifySubscribers({
						success: false,
						error: {
							type: 'PARSING_ERROR',
							message: parseError instanceof Error ? parseError.message : 'Failed to parse JSON'
						}
					});
				}
			}
		});

		this.process.stderr.on('data', (data: Buffer) => {
			console.error(`MediaManager stderr: ${data}`);
			this.notifySubscribers({
				success: false,
				error: { type: 'HELPER_ERROR', message: data.toString() }
			});
		});

		this.process.on('error', (err) => {
			console.error('Failed to start MediaManager process.', err);
			const errorType: MediaManagerErrorType = err.message.includes('ENOENT') ? 'FILE_NOT_FOUND' : 'HELPER_ERROR';
			this.notifySubscribers({
				success: false,
				error: { type: errorType, message: err.message }
			});
			this.process = null;
		});

		this.process.on('close', (code) => {
			console.log(`MediaManager process exited with code ${code}`);
			this.process = null;
		});
	}

	public stop(): void {
		if (this.process && this.subscribers.size === 0) {
			this.process.kill();
			this.process = null;
		}
	}

	public sendCommand(command: MediaCommand): void {
		if (this.process && this.process.stdin.writable) {
			this.process.stdin.write(`${command}\n`);
		}
	}
}

export const mediaManagerService = new MediaManagerService();

export function toggleMediaPlayPause(): void {
	mediaManagerService.sendCommand('toggle');
}

export function nextMedia(): void {
	mediaManagerService.sendCommand('next');
}

export function previousMedia(): void {
	mediaManagerService.sendCommand('previous');
}
