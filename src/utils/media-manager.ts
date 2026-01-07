import { execFile } from 'child_process';
import { access } from 'fs/promises';
import path from 'path';

export type MediaInfo = {
	Title?: string;
	Artist?: string;
	Artists?: string[];
	AlbumArtist?: string;
	AlbumTitle?: string;
	Status?: 'Playing' | 'Paused' | 'Stopped';
	CoverArtBase64?: string;
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

function getPlatform(): 'windows' | 'darwin' | 'linux' {
	const platform = process.platform;
	if (platform === 'win32') return 'windows';
	if (platform === 'darwin') return 'darwin';
	return 'linux';
}

function getManagerExeName(): string {
	const platform = getPlatform();
	if (platform === 'windows') return 'MediaManager.exe';
	if (platform === 'darwin') return 'MediaManager';
	return 'MediaManager';
}

function getManagerPath(): string {
	const exeName = getManagerExeName();
	return path.join(process.cwd(), 'bin', exeName);
}

async function checkManagerExists(managerPath: string): Promise<boolean> {
	try {
		await access(managerPath);
		return true;
	} catch {
		const platform = getPlatform();
		const exeName = getManagerExeName();
		console.error(`${exeName} not found at: ${managerPath}`);
		if (platform === 'windows') {
			console.error('Please build the C# project using: cd MediaManager/platforms/windows && build.bat');
		} else if (platform === 'darwin') {
			console.error('Please build the project using: cd MediaManager/platforms/macos && build.sh');
		} else {
			console.error('Please build the project for your platform');
		}
		return false;
	}
}

function parseMediaInfo(jsonString: string): MediaInfo {
	return JSON.parse(jsonString);
}

function isMediaInfoEmpty(info: MediaInfo): boolean {
	return !info.Title && !info.Artist && (!info.Artists || info.Artists.length === 0);
}

export async function getMediaInfo(): Promise<MediaManagerResult> {
	const managerPath = getManagerPath();

	if (!(await checkManagerExists(managerPath))) {
		return {
			success: false,
			error: {
				type: 'FILE_NOT_FOUND',
				message: `${getManagerExeName()} not found at: ${managerPath}`
			}
		};
	}

	return new Promise<MediaManagerResult>((resolve) => {
		execFile(managerPath, (error, stdout, stderr) => {
			if (error) {
				const errorType: MediaManagerErrorType = error.code === 'ENOENT' 
					? 'FILE_NOT_FOUND' 
					: 'HELPER_ERROR';
				
				return resolve({
					success: false,
					error: {
						type: errorType,
						message: error.message,
						code: error.code
					}
				});
			}

			if (stderr && !stdout) {
				return resolve({
					success: false,
					error: {
						type: 'HELPER_ERROR',
						message: 'Manager returned stderr'
					}
				});
			}

			if (!stdout || stdout.trim().length === 0) {
				return resolve({
					success: false,
					error: {
						type: 'NOTHING_PLAYING',
						message: 'No media is currently playing'
					}
				});
			}

			try {
				const info = parseMediaInfo(stdout);

				if (isMediaInfoEmpty(info)) {
					return resolve({
						success: false,
						error: {
							type: 'NOTHING_PLAYING',
							message: 'No media data available'
						}
					});
				}

				return resolve({
					success: true,
					data: info
				});
			} catch (parseError) {
				console.error('Failed to parse JSON from manager:', parseError);
				return resolve({
					success: false,
					error: {
						type: 'PARSING_ERROR',
						message: parseError instanceof Error ? parseError.message : 'Failed to parse JSON'
					}
				});
			}
		});
	});
}

export async function toggleMediaPlayPause(): Promise<MediaManagerResult> {
	const managerPath = getManagerPath();

	if (!(await checkManagerExists(managerPath))) {
		return {
			success: false,
			error: {
				type: 'FILE_NOT_FOUND',
				message: `${getManagerExeName()} not found at: ${managerPath}`
			}
		};
	}

	return new Promise<MediaManagerResult>((resolve) => {
		execFile(managerPath, ['toggle'], (error, stdout, stderr) => {
			if (error) {
				return resolve({
					success: false,
					error: {
						type: 'HELPER_ERROR',
						message: error.message,
						code: error.code
					}
				});
			}

			if (stderr) {
				return resolve({
					success: false,
					error: {
						type: 'HELPER_ERROR',
						message: 'Manager returned stderr'
					}
				});
			}

			return resolve({
				success: true,
				data: {} as MediaInfo
			});
		});
	});
}

