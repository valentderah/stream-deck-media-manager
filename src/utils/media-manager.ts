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

type MediaCommand = 'toggle' | 'next' | 'previous' | 'update';

class MediaManagerService {
	private process: ChildProcessWithoutNullStreams | null = null;
	private subscribers: Set<(result: MediaManagerResult) => void> = new Set();
	private buffer: string = '';
	private restartAttempts: number = 0;
	private maxRestartAttempts: number = 5;
	private restartDelay: number = 1000; // 1 секунда
	private isRestarting: boolean = false;
	private isShuttingDown: boolean = false;

	public subscribe(callback: (result: MediaManagerResult) => void): () => void {
		this.subscribers.add(callback);
		// Если есть подписчики, но процесса нет - запускаем его
		if (!this.process && !this.isRestarting) {
			this.start();
		}
		return () => {
			this.unsubscribe(callback);
		};
	}

	public unsubscribe(callback: (result: MediaManagerResult) => void): void {
		this.subscribers.delete(callback);

		if (this.subscribers.size === 0 && this.process) {
			this.isShuttingDown = true;
			this.process.kill();
			this.process = null;
			this.isShuttingDown = false;
			this.restartAttempts = 0; // Сбрасываем счётчик попыток
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

	private async restartProcess(): Promise<void> {
		if (this.isRestarting || this.isShuttingDown) return;
		if (this.subscribers.size === 0) return; // Нет подписчиков - не перезапускаем

		if (this.restartAttempts >= this.maxRestartAttempts) {
			console.error(`MediaManager: Max restart attempts (${this.maxRestartAttempts}) reached. Stopping.`);
			this.notifySubscribers({
				success: false,
				error: {
					type: 'HELPER_ERROR',
					message: `Process crashed too many times (${this.restartAttempts} attempts)`
				}
			});
			this.restartAttempts = 0;
			return;
		}

		this.isRestarting = true;
		this.restartAttempts++;
		
		console.log(`MediaManager: Attempting to restart process (attempt ${this.restartAttempts}/${this.maxRestartAttempts})...`);

		// Ждём перед перезапуском
		await new Promise(resolve => setTimeout(resolve, this.restartDelay));

		if (this.subscribers.size > 0 && !this.isShuttingDown) {
			this.start();
		}
		
		this.isRestarting = false;
	}

	public start(): void {
		if (this.process) return;

		const exeName = process.platform === 'win32' ? 'MediaManager.exe' : 'MediaManager';
		const managerPath = path.join(process.cwd(), 'bin', exeName);

		try {
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
							// Успешный ответ - сбрасываем счётчик попыток перезапуска
							this.restartAttempts = 0;
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
				// Если процесс не смог запуститься и есть подписчики - пытаемся перезапустить
				if (this.subscribers.size > 0 && !this.isShuttingDown && errorType !== 'FILE_NOT_FOUND') {
					this.restartProcess();
				}
			});

			this.process.on('close', (code) => {
				console.log(`MediaManager process exited with code ${code}`);
				const wasProcess = this.process !== null;
				this.process = null;

				// Если процесс завершился неожиданно (не по нашей инициативе) и есть подписчики - перезапускаем
				if (!this.isShuttingDown && this.subscribers.size > 0 && wasProcess) {
					this.restartProcess();
				} else {
					// Если мы сами завершили процесс или нет подписчиков - сбрасываем счётчик
					this.restartAttempts = 0;
				}
			});

		} catch (error) {
			console.error('Exception while starting MediaManager process:', error);
			this.process = null;
			this.notifySubscribers({
				success: false,
				error: {
					type: 'HELPER_ERROR',
					message: error instanceof Error ? error.message : 'Unknown error while starting process'
				}
			});
		}
	}

	public stop(): void {
		if (this.process && this.subscribers.size === 0) {
			this.isShuttingDown = true;
			this.process.kill();
			this.process = null;
			this.isShuttingDown = false;
			this.restartAttempts = 0;
		}
	}

	public sendCommand(command: MediaCommand): void {
		if (this.process && this.process.stdin.writable) {
			this.process.stdin.write(`${command}\n`);
		}
	}

	/**
	 * Запрашивает немедленное обновление текущего состояния медиа
	 */
	public requestUpdate(): void {
		this.sendCommand('update');
	}

	/**
	 * Проверяет, запущен ли процесс
	 */
	public isProcessRunning(): boolean {
		return this.process !== null && this.process.exitCode === null;
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
